using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ODataAgentSample.Demo;
using ODataAgentSample.Security;

namespace ODataAgentSample.Tests.Support;

/// <summary>Zeit, die der Test selbst vorstellt. Damit lassen sich Stunden- und Tagesfenster ohne Warten pruefen.</summary>
public sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Ersetzt Claude: liefert feste Ereignisse und merkt sich, womit er aufgerufen wurde.</summary>
public sealed class FakeDemoAgent : IDemoAgent
{
    public List<(string Prompt, string SessionId, Uri McpUrl)> Calls { get; } = [];

    public async IAsyncEnumerable<DemoEvent> RunAsync(
        string prompt, string sessionId, Uri mcpUrl, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Calls.Add((prompt, sessionId, mcpUrl));
        yield return new DemoEvent(DemoEvent.ToolCall, Id: "t1", Name: "search_countries", Input: JsonSerializer.SerializeToElement(new { text = "Island" }));
        yield return new DemoEvent(DemoEvent.ToolResult, Id: "t1", Ok: true, Summary: "21 Treffer", DurationMs: 5);
        yield return new DemoEvent(DemoEvent.TextAnswer, Text: "Island hat den ISO3-Code ISL.");
        await Task.CompletedTask;
    }
}

/// <summary>Merkt sich Aufrufe und liefert feste Ergebnisse. Prueft, was den MCP-Server ueberhaupt erreicht.</summary>
public sealed class FakeToolbox : IDemoToolbox
{
    public List<string> Called { get; } = [];

    public Task<IReadOnlyList<DemoTool>> ListToolsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DemoTool>>([]);

    public Task<DemoToolResult> CallAsync(string name, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        Called.Add(name);
        return Task.FromResult(new DemoToolResult(true, "{}"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Eine laufende App mit eingeschalteter Demo, gefaelschtem Agenten und gefaelschter Zeit.</summary>
public sealed class DemoApp : IDisposable
{
    public const string AccessCode = "zugangscode-nur-fuer-tests";
    public const string CookieKey = "cookie-schluessel-nur-fuer-tests-0123456789";
    public const string DemoApiKey = "demo-reader-key-nur-fuer-tests-0123456789";
    public const string AnthropicKey = "sk-ant-nur-fuer-tests-niemals-echt-0123456789";

    private readonly AppFactory _inner = new();
    private readonly WebApplicationFactory<Program> _factory;

    public FakeClock Clock { get; } = new();
    public FakeDemoAgent Agent { get; } = new();

    public DemoApp(Dictionary<string, string?>? overrides = null)
    {
        _factory = _inner.WithWebHostBuilder(builder =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Demo:Enabled"] = "true",
                ["Demo:AccessCodeSha256"] = ApiKeyHasher.Hash(AccessCode),
                ["Demo:CookieKey"] = CookieKey,
                ["Demo:ApiKey"] = DemoApiKey,
                ["Demo:AnthropicApiKey"] = AnthropicKey,
            };
            foreach (var (key, value) in overrides ?? [])
                settings[key] = value;
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDemoAgent>();
                services.AddSingleton<IDemoAgent>(Agent);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        });
    }

    public HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>Meldet sich an und liefert einen Client, der das Sitzungs-Cookie bei jeder Anfrage mitsendet.</summary>
    public async Task<HttpClient> LoginAsync()
    {
        var client = CreateClient();
        using var request = Post("/api/demo/login", new { code = AccessCode });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(DemoSession.CookieName + "=", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        return client;
    }

    public static HttpRequestMessage Post(string path, object body, bool csrfHeader = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
        };
        if (csrfHeader)
            request.Headers.Add("X-Demo", "1");
        return request;
    }

    public void Dispose()
    {
        _factory.Dispose();
        _inner.Dispose();
    }
}
