using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.Demo;

public sealed class DemoDisabledTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/demo")]
    [InlineData("/assets/site.css")]
    [InlineData("/api/demo/status")]
    public async Task By_default_nothing_of_the_demo_is_served(string path)
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed partial class DemoPageTests : IDisposable
{
    private readonly DemoApp _app = new();

    public void Dispose() => _app.Dispose();

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)|<style|\sstyle\s*=|\son[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineCode();

    [Theory]
    [InlineData("/")]
    [InlineData("/demo")]
    public async Task Pages_are_served_without_a_key_with_a_strict_content_security_policy(string path)
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var csp = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(InlineCode().IsMatch(html), "Die Seite enthält Inline-Code, den die Content-Security-Policy blockiert.");
        Assert.Contains("noindex", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/assets/site.css", "text/css")]
    [InlineData("/assets/landing.js", "text/javascript")]
    [InlineData("/assets/demo.js", "text/javascript")]
    [InlineData("/assets/og-image.jpg", "image/jpeg")]
    public async Task Assets_are_served_with_the_right_type(string path, string mediaType)
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/assets/gibt-es-nicht.js")]
    [InlineData("/assets/gibt-es-nicht.png")]
    [InlineData("/assets/og-image.gif")]
    [InlineData("/assets/..%2Fappsettings.json")]
    [InlineData("/assets/%2e%2e%2f%2e%2e%2fProgram.cs")]
    public async Task Only_the_known_files_are_served(string path)
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_preview_image_is_a_real_jpeg_of_reasonable_size_with_the_security_headers()
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(new Uri("/assets/og-image.jpg", UriKind.Relative));
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(bytes.Length is > 1_000 and < 150_000, $"Das Vorschaubild hat {bytes.Length} Bytes.");
        Assert.Equal([0xFF, 0xD8, 0xFF], bytes[..3]);
    }

    [Theory]
    [InlineData("/", "https://demo.vompa.dev/")]
    [InlineData("/demo", "https://demo.vompa.dev/demo")]
    public async Task Pages_carry_link_preview_metadata_that_points_to_the_served_image(string path, string url)
    {
        using var client = _app.CreateClient();

        var html = await client.GetStringAsync(new Uri(path, UriKind.Relative));

        Assert.Contains($"<meta property=\"og:url\" content=\"{url}\">", html, StringComparison.Ordinal);
        Assert.Contains($"<link rel=\"canonical\" href=\"{url}\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:image\" content=\"https://demo.vompa.dev/assets/og-image.jpg\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\">", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/demo")]
    public async Task Pages_link_to_imprint_and_privacy_policy_on_the_main_site(string path)
    {
        using var client = _app.CreateClient();

        var html = await client.GetStringAsync(new Uri(path, UriKind.Relative));

        Assert.Contains("href=\"https://vompa.dev/impressum.html\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://vompa.dev/datenschutz.html\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_post_of_the_pages_sends_the_csrf_header_the_api_requires()
    {
        using var client = _app.CreateClient();
        var landing = await client.GetStringAsync(new Uri("/assets/landing.js", UriKind.Relative));
        var demo = await client.GetStringAsync(new Uri("/assets/demo.js", UriKind.Relative));

        // Login (landing.js) sowie Chat und Logout (demo.js) sind POSTs, die ohne X-Demo mit 400 abgewiesen werden.
        Assert.Contains("'X-Demo'", landing, StringComparison.Ordinal);
        Assert.True(Regex.Count(demo, "'X-Demo'") >= 2, "Chat und Abmelden müssen beide den Header X-Demo senden.");
    }

    [Fact]
    public async Task Assets_are_not_cached_so_pages_and_styles_always_match_after_a_deployment()
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(new Uri("/assets/site.css", UriKind.Relative));

        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_pages_load_only_their_own_scripts_and_styles()
    {
        using var client = _app.CreateClient();
        var external = new Regex(@"(src|href)\s*=\s*""https?://(?!github\.com/vompa/|vompa\.dev/(impressum|datenschutz)\.html|demo\.vompa\.dev/)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));

        foreach (var path in new[] { "/", "/demo" })
        {
            var html = await client.GetStringAsync(new Uri(path, UriKind.Relative));
            Assert.False(external.IsMatch(html), $"{path} lädt etwas von einem fremden Server.");
        }
    }
}

public sealed class DemoLoginTests : IDisposable
{
    private readonly DemoApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task A_wrong_code_is_rejected()
    {
        using var client = _app.CreateClient();
        using var request = DemoApp.Post("/api/demo/login", new { code = "falsch" });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task The_right_code_sets_a_hardened_session_cookie()
    {
        using var client = _app.CreateClient();
        using var request = DemoApp.Post("/api/demo/login", new { code = DemoApp.AccessCode });

        using var response = await client.SendAsync(request);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("demo_session=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DemoApp.AccessCode, cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logging_in_without_the_csrf_header_is_rejected()
    {
        using var client = _app.CreateClient();
        using var request = DemoApp.Post("/api/demo/login", new { code = DemoApp.AccessCode }, csrfHeader: false);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("kein json")]
    [InlineData("{}")]
    [InlineData("{\"code\":\"\"}")]
    public async Task Broken_or_empty_input_is_rejected_as_a_failed_attempt(string body)
    {
        using var client = _app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/demo/login")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Demo", "1");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Guessing_is_slowed_down_after_five_failures_and_freed_again_later()
    {
        using var client = _app.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            using var wrong = DemoApp.Post("/api/demo/login", new { code = "falsch" + i });
            using var r = await client.SendAsync(wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        using var blockedCorrect = DemoApp.Post("/api/demo/login", new { code = DemoApp.AccessCode });
        using var blocked = await client.SendAsync(blockedCorrect);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));

        _app.Clock.Advance(TimeSpan.FromMinutes(11));
        using var again = DemoApp.Post("/api/demo/login", new { code = DemoApp.AccessCode });
        using var ok = await client.SendAsync(again);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Status_shows_whether_someone_is_logged_in_and_the_remaining_quota()
    {
        using var anonymous = _app.CreateClient();
        var loggedOut = JsonDocument.Parse(await anonymous.GetStringAsync(new Uri("/api/demo/status", UriKind.Relative))).RootElement;
        Assert.False(loggedOut.GetProperty("authenticated").GetBoolean());
        Assert.False(loggedOut.TryGetProperty("limits", out _));

        using var client = await _app.LoginAsync();
        var loggedIn = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/demo/status", UriKind.Relative))).RootElement;

        Assert.True(loggedIn.GetProperty("authenticated").GetBoolean());
        var limits = loggedIn.GetProperty("limits");
        Assert.Equal(100, limits.GetProperty("hourLimit").GetInt32());
        Assert.Equal(15, limits.GetProperty("sessionHourLimit").GetInt32());
        Assert.Equal(400, limits.GetProperty("dayLimit").GetInt32());
    }

    [Fact]
    public async Task A_forged_cookie_does_not_count_as_logged_in()
    {
        using var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", "demo_session=erfunden.9999999999.erfunden");

        var status = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/demo/status", UriKind.Relative))).RootElement;

        Assert.False(status.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task The_session_ends_after_the_configured_time()
    {
        using var client = await _app.LoginAsync();

        _app.Clock.Advance(TimeSpan.FromHours(9));
        var status = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/demo/status", UriKind.Relative))).RootElement;

        Assert.False(status.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task Logout_deletes_the_cookie_and_needs_the_csrf_header()
    {
        using var client = await _app.LoginAsync();

        using var forged = DemoApp.Post("/api/demo/logout", new { }, csrfHeader: false);
        using var rejected = await client.SendAsync(forged);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var request = DemoApp.Post("/api/demo/logout", new { });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("demo_session=;", response.Headers.GetValues("Set-Cookie").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_misconfigured_demo_does_not_start()
    {
        using var app = new DemoApp(new Dictionary<string, string?> { ["Demo:CookieKey"] = "kurz" });

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }
}

public sealed class DemoChatTests : IDisposable
{
    private static readonly string[] PublicPaths =
        ["/", "/demo", "/assets/demo.js", "/assets/landing.js", "/assets/site.css"];

    private static readonly string[] EvilTools = ["update_country", "web_search"];

    private static readonly string[] Secrets = [DemoApp.AnthropicKey, DemoApp.DemoApiKey, DemoApp.CookieKey, DemoApp.AccessCode];

    private readonly DemoApp _app = new();

    public void Dispose() => _app.Dispose();

    private static async Task<HttpResponseMessage> AskAsync(HttpClient client, string prompt, bool csrfHeader = true)
    {
        using var request = DemoApp.Post("/api/demo/chat", new { prompt }, csrfHeader);
        return await client.SendAsync(request);
    }

    private static List<JsonElement> Events(string body) =>
        [.. body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(chunk => chunk.Trim())
            .Where(chunk => chunk.StartsWith("data: ", StringComparison.Ordinal))
            .Select(chunk => JsonDocument.Parse(chunk["data: ".Length..]).RootElement.Clone())];

    [Fact]
    public async Task Without_a_session_nothing_is_forwarded()
    {
        using var client = _app.CreateClient();

        using var response = await AskAsync(client, "Wie viele Länder gibt es?");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_app.Agent.Calls);
    }

    [Fact]
    public async Task Without_the_csrf_header_nothing_is_forwarded()
    {
        using var client = await _app.LoginAsync();

        using var response = await AskAsync(client, "Wie viele Länder gibt es?", csrfHeader: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_app.Agent.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_question_is_rejected_and_costs_nothing(string prompt)
    {
        using var client = await _app.LoginAsync();

        using var response = await AskAsync(client, prompt);
        var status = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/demo/status", UriKind.Relative))).RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100, status.GetProperty("limits").GetProperty("hourRemaining").GetInt32());
        Assert.Empty(_app.Agent.Calls);
    }

    [Fact]
    public async Task A_question_longer_than_300_characters_is_rejected()
    {
        using var client = await _app.LoginAsync();

        using var response = await AskAsync(client, new string('x', 301));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_app.Agent.Calls);
    }

    [Fact]
    public async Task A_question_streams_the_tool_calls_and_the_answer_and_ends_with_the_remaining_quota()
    {
        using var client = await _app.LoginAsync();

        using var response = await AskAsync(client, "Was ist der ISO3-Code von Island?");
        var events = Events(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["tool_call", "tool_result", "text", "done"], events.Select(e => e.GetProperty("type").GetString()!).ToArray());
        Assert.Equal("search_countries", events[0].GetProperty("name").GetString());
        Assert.True(events[1].GetProperty("ok").GetBoolean());
        Assert.Contains("ISL", events[2].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(99, events[3].GetProperty("limits").GetProperty("hourRemaining").GetInt32());
        Assert.Equal(14, events[3].GetProperty("limits").GetProperty("sessionHourRemaining").GetInt32());
    }

    [Fact]
    public async Task The_browser_cannot_choose_model_tools_or_target()
    {
        using var client = await _app.LoginAsync();
        using var request = DemoApp.Post("/api/demo/chat", new
        {
            prompt = "Hallo",
            model = "claude-opus-5-5",
            tools = EvilTools,
            mcpUrl = "http://boese.example/mcp",
            maxTokens = 100000,
        });

        using var response = await client.SendAsync(request);
        await response.Content.ReadAsStringAsync();

        var call = Assert.Single(_app.Agent.Calls);
        Assert.Equal("Hallo", call.Prompt);
        Assert.True(call.McpUrl.IsLoopback);
        Assert.Equal("/mcp", call.McpUrl.AbsolutePath);
    }

    [Fact]
    public async Task The_sixteenth_question_in_an_hour_is_refused_with_a_wait_time_and_is_possible_again_later()
    {
        using var client = await _app.LoginAsync();
        for (var i = 0; i < 15; i++)
        {
            using var ok = await AskAsync(client, "Frage " + i);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            await ok.Content.ReadAsStringAsync();
        }

        using var refused = await AskAsync(client, "Frage 16");
        var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));
        Assert.True(body.GetProperty("retryAfterSeconds").GetInt32() > 0);
        Assert.Equal(15, _app.Agent.Calls.Count);

        _app.Clock.Advance(TimeSpan.FromMinutes(61));
        using var again = await AskAsync(client, "Frage 17");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task The_hourly_limit_is_shared_by_all_sessions()
    {
        using var small = new DemoApp(new Dictionary<string, string?> { ["Demo:HourLimit"] = "3", ["Demo:SessionHourLimit"] = "15" });
        using var first = await small.LoginAsync();
        using var second = await small.LoginAsync();

        var statuses = new List<HttpStatusCode>();
        HttpClient[] order = [first, second, first, second];
        foreach (var client in order)
        {
            using var response = await AskAsync(client, "Frage");
            statuses.Add(response.StatusCode);
            await response.Content.ReadAsStringAsync();
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task No_secret_ever_appears_in_a_response()
    {
        using var client = await _app.LoginAsync();
        var bodies = new List<string>();

        using (var chat = await AskAsync(client, "Hallo"))
            bodies.Add(await chat.Content.ReadAsStringAsync());
        bodies.Add(await client.GetStringAsync(new Uri("/api/demo/status", UriKind.Relative)));
        foreach (var path in PublicPaths)
            bodies.Add(await client.GetStringAsync(new Uri(path, UriKind.Relative)));
        using (var wrong = DemoApp.Post("/api/demo/login", new { code = "falsch" }))
        using (var response = await client.SendAsync(wrong))
            bodies.Add(await response.Content.ReadAsStringAsync());

        foreach (var secret in Secrets)
            Assert.All(bodies, body => Assert.DoesNotContain(secret, body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_demo_key_can_read_the_odata_service_but_never_change_anything()
    {
        using var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", DemoApp.DemoApiKey);

        using var read = await client.GetAsync(new Uri("/odata/v1/Countries?$top=1", UriKind.Relative));
        using var patch = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/odata/v1/Countries(1)")
        {
            Content = new StringContent("""{"DisplayNameGER":"x"}""", System.Text.Encoding.UTF8, "application/json"),
        });
        using var delete = await client.DeleteAsync(new Uri("/odata/v1/Countries(1)", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }
}
