namespace ODataAgentSample.Agent;

/// <summary>Flaches, token-sparsames Abbild eines Landes fuer Agenten (statt der verschachtelten OData-Entitaet).</summary>
public sealed record CountryInfo(
    int Id,
    string? Iso2,
    string? Iso3,
    string Name,
    string DisplayNameGer,
    bool EuMember,
    bool EwrMember,
    string? WorldRegion,
    string? CountryRegion);

public sealed record CountrySearchResult(int Total, int Offset, bool HasMore, IReadOnlyList<CountryInfo> Items);

public sealed record SubRegionInfo(string Iso, string Name, string NameGer, int Countries);

public sealed record WorldRegionInfo(
    string Iso,
    string Name,
    string NameGer,
    int Countries,
    int EuMembers,
    IReadOnlyList<SubRegionInfo> SubRegions);
