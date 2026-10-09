using System.Text.Json;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ODataAgentSample.Demo;
using ODataAgentSample.Security;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.Demo;

public sealed class DemoQuotaTests
{
    private static DemoOptions Options(int hour = 100, int session = 15, int day = 400) =>
        new() { HourLimit = hour, SessionHourLimit = session, DayLimit = day };

    [Fact]
    public void Allows_exactly_the_hourly_limit_for_everyone_together()
    {
        var quota = new DemoQuota(Options(hour: 100, session: 1000, day: 1000), new FakeClock());

        var allowed = Enumerable.Range(0, 150).Count(i => quota.TryAcquire("s" + i).Allowed);

        Assert.Equal(100, allowed);
    }

    [Fact]
    public void Allows_exactly_the_session_limit_per_session_and_other_sessions_still_work()
    {
        var quota = new DemoQuota(Options(), new FakeClock());

        var first = Enumerable.Range(0, 30).Count(_ => quota.TryAcquire("a").Allowed);

        Assert.Equal(15, first);
        Assert.True(quota.TryAcquire("b").Allowed);
    }

    [Fact]
    public void Stops_at_the_daily_limit_even_when_the_hourly_limit_is_free_again()
    {
        var clock = new FakeClock();
        var quota = new DemoQuota(Options(hour: 100, session: 1000, day: 250), clock);

        var allowed = 0;
        for (var hour = 0; hour < 5; hour++)
        {
            allowed += Enumerable.Range(0, 100).Count(i => quota.TryAcquire($"h{hour}-{i}").Allowed);
            clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        }

        Assert.Equal(250, allowed);
    }

    [Fact]
    public void Recovers_after_the_window_and_reports_how_long_to_wait()
    {
        var clock = new FakeClock();
        var quota = new DemoQuota(Options(hour: 2, session: 1000, day: 1000), clock);
        quota.TryAcquire("a");
        clock.Advance(TimeSpan.FromMinutes(10));
        quota.TryAcquire("b");

        var denied = quota.TryAcquire("c");

        Assert.False(denied.Allowed);
        Assert.InRange(denied.RetryAfter, TimeSpan.FromMinutes(49), TimeSpan.FromMinutes(51));

        clock.Advance(TimeSpan.FromMinutes(51));
        Assert.True(quota.TryAcquire("c").Allowed);
    }

    [Fact]
    public void Denied_requests_do_not_use_up_the_quota()
    {
        var quota = new DemoQuota(Options(hour: 100, session: 2, day: 1000), new FakeClock());
        quota.TryAcquire("a");
        quota.TryAcquire("a");

        for (var i = 0; i < 50; i++)
            Assert.False(quota.TryAcquire("a").Allowed);

        Assert.Equal(98, quota.Limits("other").HourRemaining);
    }

    [Fact]
    public void Parallel_requests_cannot_exceed_the_limit()
    {
        var quota = new DemoQuota(Options(hour: 100, session: 100000, day: 100000), new FakeClock());
        var allowed = 0;

        Parallel.For(0, 2000, i =>
        {
            if (quota.TryAcquire("s" + (i % 50)).Allowed)
                Interlocked.Increment(ref allowed);
        });

        Assert.Equal(100, allowed);
    }
}

public sealed class DemoSessionTests
{
    private const string Key = "ein-langer-schluessel-nur-fuer-tests-0123456789";
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_cookie_validates_and_returns_the_session_id()
    {
        var cookie = DemoSession.Create(Key, Now.AddHours(8), out var id);

        Assert.Equal(id, DemoSession.Validate(Key, cookie, Now));
    }

    [Fact]
    public void An_expired_cookie_is_rejected()
    {
        var cookie = DemoSession.Create(Key, Now.AddHours(8), out _);

        Assert.Null(DemoSession.Validate(Key, cookie, Now.AddHours(8).AddSeconds(1)));
    }

    [Fact]
    public void A_cookie_signed_with_another_key_is_rejected()
    {
        var cookie = DemoSession.Create("anderer-schluessel-nur-fuer-tests-0123456789", Now.AddHours(8), out _);

        Assert.Null(DemoSession.Validate(Key, cookie, Now));
    }

    [Fact]
    public void A_tampered_expiry_or_id_is_rejected()
    {
        var cookie = DemoSession.Create(Key, Now.AddHours(1), out _);
        var parts = cookie.Split('.');
        var longerExpiry = $"{parts[0]}.{Now.AddYears(5).ToUnixTimeSeconds()}.{parts[2]}";
        var otherId = $"fremd.{parts[1]}.{parts[2]}";

        Assert.Null(DemoSession.Validate(Key, longerExpiry, Now));
        Assert.Null(DemoSession.Validate(Key, otherId, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("a.-1.c")]
    [InlineData("...")]
    public void Garbage_is_rejected(string? cookie) => Assert.Null(DemoSession.Validate(Key, cookie, Now));

    [Fact]
    public void An_overlong_cookie_is_rejected()
    {
        Assert.Null(DemoSession.Validate(Key, new string('a', 500), Now));
    }
}

public sealed class DemoLoginThrottleTests
{
    [Fact]
    public void Blocks_a_client_after_five_failures_and_frees_it_after_ten_minutes()
    {
        var clock = new FakeClock();
        var throttle = new DemoLoginThrottle(clock);
        for (var i = 0; i < 4; i++)
            throttle.RecordFailure("1.2.3.4");
        Assert.False(throttle.IsBlocked("1.2.3.4", out _));

        throttle.RecordFailure("1.2.3.4");

        Assert.True(throttle.IsBlocked("1.2.3.4", out var wait));
        Assert.InRange(wait, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));
        Assert.False(throttle.IsBlocked("5.6.7.8", out _));

        clock.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));
        Assert.False(throttle.IsBlocked("1.2.3.4", out _));
    }

    [Fact]
    public void Blocks_everyone_when_many_clients_fail_together()
    {
        var throttle = new DemoLoginThrottle(new FakeClock());
        for (var i = 0; i < DemoLoginThrottle.MaxFailuresTotal; i++)
            throttle.RecordFailure("client-" + i);

        Assert.True(throttle.IsBlocked("noch-nie-gesehen", out _));
    }
}

public sealed class DemoOptionsTests
{
    private static DemoOptions Valid() => new()
    {
        Enabled = true,
        AccessCodeSha256 = ApiKeyHasher.Hash("code"),
        CookieKey = new string('k', 40),
        ApiKey = new string('a', 40),
        AnthropicApiKey = "sk-test",
    };

    [Fact]
    public void Disabled_needs_no_configuration() => Assert.Null(new DemoOptions().Validate());

    [Fact]
    public void A_complete_configuration_is_valid() => Assert.Null(Valid().Validate());

    [Fact]
    public void Each_missing_secret_is_reported()
    {
        Assert.Contains("AccessCodeSha256", Assert.IsType<string>(Change(o => o.AccessCodeSha256 = "zu-kurz")), StringComparison.Ordinal);
        Assert.Contains("CookieKey", Assert.IsType<string>(Change(o => o.CookieKey = "kurz")), StringComparison.Ordinal);
        Assert.Contains("Demo:ApiKey", Assert.IsType<string>(Change(o => o.ApiKey = "kurz")), StringComparison.Ordinal);
        Assert.Contains("AnthropicApiKey", Assert.IsType<string>(Change(o => o.AnthropicApiKey = " ")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com/mcp")]
    [InlineData("http://example.com/mcp")]
    [InlineData("http://10.0.0.5/mcp")]
    [InlineData("kein-url")]
    public void The_mcp_target_must_be_a_loopback_address(string url)
    {
        Assert.Contains("McpUrl", Assert.IsType<string>(Change(o => o.McpUrl = url)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost:8080/mcp")]
    [InlineData("http://127.0.0.1:5240/mcp")]
    public void A_loopback_mcp_target_is_accepted(string url) => Assert.Null(Change(o => o.McpUrl = url));

    [Fact]
    public void The_validator_reports_errors_at_startup()
    {
        var bad = Valid();
        bad.CookieKey = "kurz";

        var result = new DemoOptionsValidator().Validate(null, bad);

        Assert.True(result.Failed);
    }

    private static string? Change(Action<DemoOptions> edit)
    {
        var options = Valid();
        edit(options);
        return options.Validate();
    }
}

public sealed class DemoToolPolicyTests
{
    [Theory]
    [InlineData("search_countries")]
    [InlineData("get_country")]
    [InlineData("list_regions")]
    public void Read_tools_are_allowed(string name) => Assert.True(DemoToolPolicy.IsAllowed(name));

    [Theory]
    [InlineData("update_country")]
    [InlineData("SEARCH_COUNTRIES")]
    [InlineData("search_countries ")]
    [InlineData("web_search")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_not_allowed(string? name) => Assert.False(DemoToolPolicy.IsAllowed(name));

    [Fact]
    public void Only_the_three_read_tools_are_on_the_list() =>
        Assert.Equal(["get_country", "list_regions", "search_countries"], DemoToolPolicy.Allowed.Order(StringComparer.Ordinal).ToArray());

    [Fact]
    public async Task A_tool_that_is_not_allowed_never_reaches_the_mcp_server()
    {
        var toolbox = new FakeToolbox();

        var result = await ClaudeDemoAgent.ExecuteAsync(
            "update_country", new Dictionary<string, JsonElement>(), toolbox, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Empty(toolbox.Called);
    }

    [Fact]
    public async Task An_allowed_tool_is_forwarded_to_the_mcp_server()
    {
        var toolbox = new FakeToolbox();

        var result = await ClaudeDemoAgent.ExecuteAsync(
            "list_regions", new Dictionary<string, JsonElement>(), toolbox, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(["list_regions"], toolbox.Called);
    }
}

public sealed class DemoRequestToClaudeTests
{
    private static readonly JsonElement EmptySchema = JsonDocument.Parse("""{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}""").RootElement;

    private static IReadOnlyList<DemoTool> OfferedByServer() =>
    [
        new DemoTool("search_countries", "Suche", EmptySchema),
        new DemoTool("get_country", "Land", EmptySchema),
        new DemoTool("list_regions", "Regionen", EmptySchema),
        new DemoTool("update_country", "AENDERT ein Land", EmptySchema),
        new DemoTool("irgendein_anderes_tool", "x", EmptySchema),
    ];

    private static MessageCreateParams Build(DemoOptions? options = null) =>
        ClaudeDemoAgent.BuildParameters(options ?? new DemoOptions(), OfferedByServer(), [new() { Role = Role.User, Content = "Hallo" }], "hash");

    private static string[] ToolNames(MessageCreateParams parameters) =>
        [.. parameters.Tools!.Select(t => t.TryPickTool(out Tool? tool) ? tool.Name : "<kein-eigenes-tool>")];

    [Fact]
    public void Claude_only_gets_the_allowed_read_tools_even_if_the_server_offers_more()
    {
        Assert.Equal(["get_country", "list_regions", "search_countries"], ToolNames(Build()).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void There_are_no_built_in_claude_tools_and_no_forced_tool_choice()
    {
        var parameters = Build();

        Assert.All(parameters.Tools!, t => Assert.True(t.TryPickTool(out _)));
        Assert.Null(parameters.ToolChoice);
    }

    [Fact]
    public void Model_and_output_limit_come_from_the_configuration()
    {
        var parameters = Build(new DemoOptions { Model = "claude-haiku-5-5", MaxOutputTokens = 512 });

        Assert.Equal("claude-haiku-5-5", parameters.Model.ToString().Trim('"'));
        Assert.Equal(512, parameters.MaxTokens);
    }

    [Fact]
    public void The_system_prompt_restricts_the_assistant_to_the_country_data_and_keeps_itself_private()
    {
        Assert.Contains("nur lesend", ClaudeDemoAgent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Länderdaten", ClaudeDemoAgent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("nicht wieder", ClaudeDemoAgent.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tool_schema_of_the_server_is_passed_on()
    {
        var tool = ClaudeDemoAgent.ToAnthropicTool(new DemoTool("search_countries", "Suche", EmptySchema));

        Assert.Equal("search_countries", tool.Name);
        Assert.Contains("text", tool.InputSchema.Properties!.Keys);
        Assert.Contains("text", tool.InputSchema.Required!);
    }
}

public sealed class DemoAgentFailureTests
{
    private sealed class FailingFactory(string message) : IDemoToolboxFactory
    {
        public Task<IDemoToolbox> ConnectAsync(Uri mcpUrl, string apiKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }

    [Fact]
    public async Task Errors_reach_the_browser_without_details_that_could_reveal_secrets()
    {
        const string secret = "sk-ant-dieser-schluessel-darf-nirgends-auftauchen";
        var options = new DemoOptions { Enabled = true, AnthropicApiKey = secret, ApiKey = new string('a', 40) };
        using var agent = new ClaudeDemoAgent(Options.Create(options), new FailingFactory("Verbindung mit " + secret + " fehlgeschlagen"), NullLogger<ClaudeDemoAgent>.Instance);

        var events = new List<DemoEvent>();
        await foreach (var e in agent.RunAsync("Frage", "sitzung", new Uri("http://127.0.0.1:1/mcp"), CancellationToken.None))
            events.Add(e);

        var error = Assert.Single(events);
        Assert.Equal(DemoEvent.Error, error.Type);
        Assert.DoesNotContain("sk-ant", JsonSerializer.Serialize(error), StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_results_are_summarised_for_the_trace()
    {
        Assert.Equal("21 Treffer", DemoSummary.Of(new DemoToolResult(true, """{"total":21,"offset":0,"hasMore":false,"items":[]}""")));
        Assert.Equal("21 Treffer (weitere vorhanden)", DemoSummary.Of(new DemoToolResult(true, """{"total":21,"hasMore":true}""")));
        Assert.Equal("6 Einträge", DemoSummary.Of(new DemoToolResult(true, "[1,2,3,4,5,6]")));
        Assert.StartsWith("Kein Land", DemoSummary.Of(new DemoToolResult(false, "Kein Land zu 'Island' gefunden.")), StringComparison.Ordinal);
        Assert.True(DemoSummary.Of(new DemoToolResult(false, new string('x', 1000))).Length < 320);
    }
}
