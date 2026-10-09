using System.Text.Json;

namespace ODataAgentSample.Demo;

/// <summary>Ein Ereignis des Chat-Streams zur Seite. Nur die fuer den Typ relevanten Felder sind gesetzt.</summary>
public sealed record DemoEvent(
    string Type,
    string? Id = null,
    string? Name = null,
    JsonElement? Input = null,
    bool? Ok = null,
    string? Summary = null,
    long? DurationMs = null,
    string? Text = null,
    string? Message = null,
    DemoLimits? Limits = null)
{
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    public const string TextAnswer = "text";
    public const string Error = "error";
    public const string Done = "done";
}

/// <summary>Fuehrt eine Frage aus: Claude plus die erlaubten MCP-Tools. Ersetzbar, damit Tests ohne echtes Modell laufen.</summary>
public interface IDemoAgent
{
    IAsyncEnumerable<DemoEvent> RunAsync(string prompt, string sessionId, Uri mcpUrl, CancellationToken cancellationToken);
}

public sealed record DemoTool(string Name, string Description, JsonElement InputSchema);

public sealed record DemoToolResult(bool Ok, string Text);

/// <summary>Verbindung zum MCP-Server. Nur diese Schnittstelle fuehrt Tool-Aufrufe aus.</summary>
public interface IDemoToolbox : IAsyncDisposable
{
    Task<IReadOnlyList<DemoTool>> ListToolsAsync(CancellationToken cancellationToken);

    Task<DemoToolResult> CallAsync(string name, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken);
}

public interface IDemoToolboxFactory
{
    Task<IDemoToolbox> ConnectAsync(Uri mcpUrl, string apiKey, CancellationToken cancellationToken);
}
