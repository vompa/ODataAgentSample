using System.Net;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.Agent;

public sealed class McpEndpointTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private async Task<McpClient> ConnectAsync(string key)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = key },
            },
            factory.CreateClient(),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    [Fact]
    public async Task Mcp_endpoint_requires_authentication()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Agent_discovers_the_four_tools_with_read_only_hints()
    {
        await using var client = await ConnectAsync(AppFactory.ReaderKey);

        var tools = await client.ListToolsAsync();

        Assert.Equal(["get_country", "list_regions", "search_countries", "update_country"], tools.Select(t => t.Name).Order());
        Assert.True(tools.Single(t => t.Name == "search_countries").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(tools.Single(t => t.Name == "update_country").ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task Agent_can_search_countries_end_to_end()
    {
        await using var client = await ConnectAsync(AppFactory.ReaderKey);

        var result = await client.CallToolAsync("search_countries", new Dictionary<string, object?>
        {
            ["text"] = "switzerland",
            ["limit"] = 5,
        });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("CHE", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_country_returns_a_tool_error_the_agent_can_act_on()
    {
        await using var client = await ConnectAsync(AppFactory.ReaderKey);

        var result = await client.CallToolAsync("get_country", new Dictionary<string, object?> { ["code"] = "Atlantis" });

        Assert.True(result.IsError);
        Assert.Contains("ISO2", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reader_key_cannot_use_update_country()
    {
        await using var client = await ConnectAsync(AppFactory.ReaderKey);

        var result = await client.CallToolAsync("update_country", new Dictionary<string, object?>
        {
            ["code"] = "LI",
            ["displayNameGer"] = "Hacked",
        });

        Assert.True(result.IsError);
        Assert.Contains("writer", TextOf(result), StringComparison.Ordinal);

        var check = await client.CallToolAsync("get_country", new Dictionary<string, object?> { ["code"] = "LI" });
        Assert.DoesNotContain("Hacked", TextOf(check), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writer_key_can_update_a_country()
    {
        await using var client = await ConnectAsync(AppFactory.WriterKey);

        var result = await client.CallToolAsync("update_country", new Dictionary<string, object?>
        {
            ["code"] = "MC",
            ["displayNameGer"] = "Fürstentum Monaco",
        });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("Monaco", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writer_updating_an_unknown_country_gets_a_tool_error_with_guidance()
    {
        await using var client = await ConnectAsync(AppFactory.WriterKey);

        var result = await client.CallToolAsync("update_country", new Dictionary<string, object?>
        {
            ["code"] = "Atlantis",
            ["displayNameGer"] = "Atlantis",
        });
        var check = await client.CallToolAsync("get_country", new Dictionary<string, object?> { ["code"] = "Atlantis" });

        Assert.True(result.IsError);
        var text = TextOf(result);
        Assert.Contains("Atlantis", text, StringComparison.Ordinal);
        Assert.Contains("ISO2", text, StringComparison.Ordinal);
        Assert.Contains("ISO3", text, StringComparison.Ordinal);
        Assert.True(check.IsError);
    }

    [Fact]
    public async Task Reader_lists_the_six_world_regions_as_typed_data()
    {
        await using var client = await ConnectAsync(AppFactory.ReaderKey);

        var result = await client.CallToolAsync("list_regions");
        var regions = JsonSerializer.Deserialize<List<WorldRegionDto>>(TextOf(result), JsonSerializerOptions.Web);

        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(regions);
        Assert.Equal(["AF", "AS", "EU", "NA", "OC", "SA"], regions.Select(r => r.Iso).Order());
        Assert.All(regions, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
            Assert.True(r.Countries > 0);
            Assert.InRange(r.EuMembers, 0, r.Countries);
        });
        Assert.Equal(264, regions.Sum(r => r.Countries));
    }

    private sealed record WorldRegionDto(string Iso, string Name, int Countries, int EuMembers);

    [Fact]
    public async Task Wrong_key_is_rejected_when_connecting() =>
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync("wrong-key"));
}
