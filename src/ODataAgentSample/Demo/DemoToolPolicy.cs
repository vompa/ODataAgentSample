namespace ODataAgentSample.Demo;

/// <summary>
/// Welche MCP-Tools Claude in der Demo ueberhaupt sehen und aufrufen darf. Nur lesende Tools des eigenen Servers.
/// <c>update_country</c> ist bewusst nicht dabei: Die Demo fragt nur ab. Zusaetzlich verhindert die Reader-Rolle
/// serverseitig jede Aenderung (403), selbst wenn diese Liste einmal falsch waere.
/// </summary>
public static class DemoToolPolicy
{
    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal) { "search_countries", "get_country", "list_regions" };

    public static bool IsAllowed(string? name) => name is not null && Allowed.Contains(name);
}
