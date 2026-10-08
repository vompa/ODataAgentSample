namespace ODataAgentSample.OData;

/// <summary>
/// Die deutschen Suchbegriffe des Original-ODataSample ($search=LaenderInEuropa) auf die Id der Welt-Region.
/// Eine Quelle fuer OData-$search und fuer die Agent-Tools.
/// </summary>
public static class WorldRegionAliases
{
    public static readonly IReadOnlyDictionary<string, int> ByTerm = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["LaenderInAfrika"] = 903,
        ["LaenderInCaribic"] = 904,
        ["LaenderInAmerika"] = 905,
        ["LaenderInEuropa"] = 908,
        ["LaenderInOzeanien"] = 909,
        ["LaenderInAsien"] = 935,
    };
}
