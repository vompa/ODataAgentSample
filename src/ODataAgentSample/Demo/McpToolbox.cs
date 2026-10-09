using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ODataAgentSample.Security;

namespace ODataAgentSample.Demo;

/// <summary>Echter MCP-Client gegen den eigenen /mcp-Endpunkt, mit dem Reader-Key der Demo.</summary>
public sealed class McpToolboxFactory : IDemoToolboxFactory
{
    public async Task<IDemoToolbox> ConnectAsync(Uri mcpUrl, string apiKey, CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = mcpUrl,
            AdditionalHeaders = new Dictionary<string, string> { [ApiKeyDefaults.HeaderName] = apiKey },
        });
        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        return new McpToolbox(client);
    }
}

internal sealed class McpToolbox(McpClient client) : IDemoToolbox
{
    public async Task<IReadOnlyList<DemoTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        return tools.Select(t => new DemoTool(t.Name, t.Description ?? "", t.JsonSchema.Clone())).ToList();
    }

    public async Task<DemoToolResult> CallAsync(string name, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var args = arguments.ToDictionary(a => a.Key, a => (object?)a.Value, StringComparer.Ordinal);
        try
        {
            var result = await client.CallToolAsync(name, args, cancellationToken: cancellationToken);
            var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            return new DemoToolResult(result.IsError != true, text);
        }
        catch (McpException ex)
        {
            // Fehlertexte des eigenen Servers sind absichtlich lesbar (Agenten sollen daraus lernen).
            return new DemoToolResult(false, ex.Message);
        }
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
