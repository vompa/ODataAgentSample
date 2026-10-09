using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using ODataAgentSample.Security;

namespace ODataAgentSample.Demo;

/// <summary>
/// Schnittstelle der Demo-Seiten: Anmelden mit gemeinsamem Zugangscode, Status und Chat als Ereignisstrom (SSE).
/// Alle Pfade sind anonym erreichbar (die Anmeldung prueft der Code selbst). Der Browser liefert nur den Prompt-Text,
/// niemals Modell, Tools, Ziel-Adresse oder Grenzen.
/// </summary>
public static class DemoEndpoints
{
    private const string CsrfHeader = "X-Demo";
    private const int MaxBodyBytes = 4096;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/demo").AllowAnonymous();
        api.MapPost("/login", LoginAsync);
        api.MapPost("/logout", Logout);
        api.MapGet("/status", Status);
        api.MapPost("/chat", ChatAsync);
    }

    private sealed record LoginRequest(string? Code);

    private sealed record ChatRequest(string? Prompt);

    private static async Task LoginAsync(
        HttpContext context, IOptions<DemoOptions> options, DemoLoginThrottle throttle, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!HasCsrfHeader(context))
        {
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new { error = "Ungültige Anfrage." }, cancellationToken);
            return;
        }

        var client = ClientKey(context);
        if (throttle.IsBlocked(client, out var wait))
        {
            await WriteTooManyAsync(context, wait, "Zu viele Fehlversuche. Bitte später noch einmal versuchen.", cancellationToken);
            return;
        }

        var request = await ReadBodyAsync<LoginRequest>(context, cancellationToken);
        var code = request?.Code;
        if (code is null || code.Length is 0 or > 200 || !CodeMatches(options.Value, code))
        {
            throttle.RecordFailure(client);
            await WriteJsonAsync(context, StatusCodes.Status401Unauthorized, new { error = "Der Zugangscode ist nicht gültig." }, cancellationToken);
            return;
        }

        var settings = options.Value;
        var expires = clock.GetUtcNow().AddHours(settings.SessionHours);
        var cookie = DemoSession.Create(settings.CookieKey, expires, out _);
        context.Response.Cookies.Append(DemoSession.CookieName, cookie, CookieSettings(context, TimeSpan.FromHours(settings.SessionHours)));
        await WriteJsonAsync(context, StatusCodes.Status200OK, new { ok = true }, cancellationToken);
    }

    private static IResult Logout(HttpContext context)
    {
        if (!HasCsrfHeader(context))
            return Results.Json(new { error = "Ungültige Anfrage." }, Json, statusCode: StatusCodes.Status400BadRequest);

        context.Response.Cookies.Delete(DemoSession.CookieName, CookieSettings(context, TimeSpan.Zero));
        return Results.NoContent();
    }

    private static IResult Status(HttpContext context, IOptions<DemoOptions> options, DemoQuota quota, TimeProvider clock)
    {
        var sessionId = SessionId(context, options.Value, clock);
        return sessionId is null
            ? Results.Json(new { authenticated = false }, Json)
            : Results.Json(new { authenticated = true, limits = quota.Limits(sessionId) }, Json);
    }

    private static async Task ChatAsync(
        HttpContext context, IOptions<DemoOptions> options, DemoQuota quota, IDemoAgent agent, TimeProvider clock,
        CancellationToken requestAborted)
    {
        var settings = options.Value;

        if (!HasCsrfHeader(context))
        {
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new { error = "Ungültige Anfrage." }, requestAborted);
            return;
        }

        var sessionId = SessionId(context, settings, clock);
        if (sessionId is null)
        {
            await WriteJsonAsync(context, StatusCodes.Status401Unauthorized, new { error = "Nicht angemeldet." }, requestAborted);
            return;
        }

        var request = await ReadBodyAsync<ChatRequest>(context, requestAborted);
        var prompt = request?.Prompt?.Trim();
        if (string.IsNullOrEmpty(prompt))
        {
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new { error = "Bitte eine Frage eingeben." }, requestAborted);
            return;
        }

        if (prompt.Length > settings.MaxPromptLength)
        {
            await WriteJsonAsync(
                context, StatusCodes.Status413PayloadTooLarge,
                new { error = $"Die Frage ist zu lang (höchstens {settings.MaxPromptLength} Zeichen)." }, requestAborted);
            return;
        }

        var quotaResult = quota.TryAcquire(sessionId);
        if (!quotaResult.Allowed)
        {
            await WriteTooManyAsync(context, quotaResult.RetryAfter, "Das Kontingent ist aufgebraucht.", requestAborted, quotaResult.Limits);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        // Ziel ist immer der eigene Server (eigener Port oder konfigurierte Loopback-Adresse), nie etwas aus der Anfrage.
        var mcpUrl = settings.McpUrl is { } configured
            ? new Uri(configured)
            : new Uri($"http://127.0.0.1:{context.Connection.LocalPort}/mcp");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RunTimeoutSeconds));

        try
        {
            await foreach (var demoEvent in agent.RunAsync(prompt, sessionId, mcpUrl, timeout.Token))
                await WriteEventAsync(context, demoEvent, requestAborted);
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
        {
            await WriteEventAsync(context, new DemoEvent(DemoEvent.Error, Message: "Zeitlimit überschritten. Bitte eine einfachere Frage stellen."), requestAborted);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await WriteEventAsync(context, new DemoEvent(DemoEvent.Done, Limits: quota.Limits(sessionId)), requestAborted);
    }

    private static bool HasCsrfHeader(HttpContext context) =>
        string.Equals(context.Request.Headers[CsrfHeader], "1", StringComparison.Ordinal);

    private static string? SessionId(HttpContext context, DemoOptions options, TimeProvider clock) =>
        DemoSession.Validate(options.CookieKey, context.Request.Cookies[DemoSession.CookieName], clock.GetUtcNow());

    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt";

    private static bool CodeMatches(DemoOptions options, string code)
    {
        if (!ApiKeyHasher.TryParseHash(options.AccessCodeSha256, out var expected))
            return false;

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    private static CookieOptions CookieSettings(HttpContext context, TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
        MaxAge = lifetime == TimeSpan.Zero ? null : lifetime,
    };

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
        where T : class
    {
        var sizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
            sizeLimit.MaxRequestBodySize = MaxBodyBytes;

        try
        {
            return await context.Request.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            return null;
        }
    }

    private static async Task WriteTooManyAsync(
        HttpContext context, TimeSpan wait, string message, CancellationToken cancellationToken, DemoLimits? limits = null)
    {
        var seconds = (int)Math.Ceiling(Math.Max(1, wait.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await WriteJsonAsync(
            context, StatusCodes.Status429TooManyRequests, new { error = message, retryAfterSeconds = seconds, limits }, cancellationToken);
    }

    private static async Task WriteJsonAsync(HttpContext context, int statusCode, object body, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(body, Json, cancellationToken);
    }

    private static async Task WriteEventAsync(HttpContext context, DemoEvent demoEvent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(demoEvent, Json);
        await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
}
