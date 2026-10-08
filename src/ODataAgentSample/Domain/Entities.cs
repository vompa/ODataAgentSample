namespace ODataAgentSample.Domain;

public abstract class BaseEntity
{
    public int Id { get; set; }
}

public class WorldRegion : BaseEntity
{
    public string ISO { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameGER { get; set; } = "";

    public ICollection<CountryRegion>? CountryRegions { get; set; }
    public ICollection<Country>? Countries { get; set; }
}

public class CountryRegion : BaseEntity
{
    public string ISO { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameGER { get; set; } = "";

    public WorldRegion? WorldRegion { get; set; }
    public int? WorldRegionId { get; set; }

    public ICollection<Country>? Countries { get; set; }
}

public class Country : BaseEntity
{
    public string DisplayName { get; set; } = "";
    public string DisplayNameGER { get; set; } = "";

    /// <summary>EU-Mitglied. Quelldaten kodieren uneinheitlich (Y/N, ein Datensatz J) - siehe <see cref="Flags"/>.</summary>
    public string? EU { get; set; }

    /// <summary>EWR-Mitglied, gleiche Kodierung wie <see cref="EU"/>.</summary>
    public string? EWR { get; set; }

    public string? ISO2 { get; set; }
    public string? ISO3 { get; set; }
    public string Name { get; set; } = "";
    public string NameGER { get; set; } = "";

    public CountryRegion? CountryRegion { get; set; }
    public int? CountryRegionId { get; set; }

    public WorldRegion? WorldRegion { get; set; }
    public int? WorldRegionId { get; set; }
}

/// <summary>Normalisiert die uneinheitliche Ja/Nein-Kodierung der Quelldaten an einer Stelle.</summary>
public static class Flags
{
    public static bool IsYes(string? value) =>
        string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "J", StringComparison.OrdinalIgnoreCase);
}
