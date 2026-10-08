using Microsoft.EntityFrameworkCore;
using ODataAgentSample.Domain;
using ODataAgentSample.Infrastructure.Db;
using ODataAgentSample.OData;

namespace ODataAgentSample.Agent;

/// <summary>
/// Typisierte Fachabfragen für Agenten. Wo ein OData-Client frei $filter/$expand/$apply formuliert,
/// bekommt der Agent wenige, eng gefasste Operationen mit festen Limits.
/// </summary>
public sealed class CountryQueryService(AppDbContext db)
{
    public const int MaxLimit = 100;
    public const int DefaultLimit = 25;
    private const int MaxNameLength = 200;

    public async Task<CountrySearchResult> SearchAsync(
        string? text, string? worldRegion, bool? euMember, bool? ewrMember, int limit, int offset, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        offset = Math.Max(offset, 0);

        var query = db.Countries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(text))
        {
            var pattern = "%" + EscapeLike(text.Trim()) + "%";
            query = query.Where(c =>
                EF.Functions.Like(c.Name, pattern, LikeEscape) || EF.Functions.Like(c.NameGER, pattern, LikeEscape)
                || EF.Functions.Like(c.DisplayName, pattern, LikeEscape) || EF.Functions.Like(c.DisplayNameGER, pattern, LikeEscape)
                || (c.ISO2 != null && EF.Functions.Like(c.ISO2, pattern, LikeEscape))
                || (c.ISO3 != null && EF.Functions.Like(c.ISO3, pattern, LikeEscape)));
        }

        if (!string.IsNullOrWhiteSpace(worldRegion))
        {
            var regionId = await ResolveWorldRegionIdAsync(worldRegion, ct);
            query = query.Where(c => c.WorldRegionId == regionId);
        }

        if (euMember is { } eu)
            query = query.Where(c => (c.EU == "Y" || c.EU == "J") == eu);
        if (ewrMember is { } ewr)
            query = query.Where(c => (c.EWR == "Y" || c.EWR == "J") == ewr);

        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderBy(c => c.Name).ThenBy(c => c.Id).Skip(offset).Take(limit)).ToListAsync(ct);

        return new CountrySearchResult(total, offset, offset + items.Count < total, items);
    }

    public async Task<CountryInfo> GetAsync(string code, CancellationToken ct) =>
        await Project(Filter(db.Countries.AsNoTracking(), code)).FirstOrDefaultAsync(ct)
        ?? throw NotFound(code);

    public async Task<IReadOnlyList<WorldRegionInfo>> ListRegionsAsync(CancellationToken ct)
    {
        var worlds = await db.WorldRegions.AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new
            {
                w.ISO,
                w.Name,
                w.NameGER,
                Countries = w.Countries!.Count,
                EuMembers = w.Countries!.Count(c => c.EU == "Y" || c.EU == "J"),
                SubRegions = w.CountryRegions!
                    .OrderBy(r => r.Name)
                    .Select(r => new SubRegionInfo(r.ISO, r.Name, r.NameGER, r.Countries!.Count))
                    .ToList(),
            })
            .ToListAsync(ct);

        return worlds
            .Select(w => new WorldRegionInfo(w.ISO, w.Name, w.NameGER, w.Countries, w.EuMembers, w.SubRegions))
            .ToList();
    }

    /// <summary>Ändert nur die übergebenen Felder und gibt den neuen Stand zurück.</summary>
    public async Task<CountryInfo> UpdateAsync(
        string code, string? displayNameGer, bool? euMember, bool? ewrMember, CancellationToken ct)
    {
        if (displayNameGer is null && euMember is null && ewrMember is null)
            throw new AgentInputException("Es wurde kein Feld zum Ändern angegeben.");

        var country = await Filter(db.Countries, code).FirstOrDefaultAsync(ct) ?? throw NotFound(code);

        if (displayNameGer is not null) country.DisplayNameGER = ValidateName(displayNameGer, "displayNameGer");
        if (euMember is { } eu) country.EU = eu ? "Y" : "N";
        if (ewrMember is { } ewr) country.EWR = ewr ? "Y" : "N";

        await db.SaveChangesAsync(ct);
        return await Project(db.Countries.AsNoTracking().Where(c => c.Id == country.Id)).SingleAsync(ct);
    }

    private const string LikeEscape = "\\";

    /// <summary>2 Zeichen = ISO2, 3 Zeichen = ISO3, sonst exakter Name (deutsch oder englisch, ohne Beachtung der Groß-/Kleinschreibung).</summary>
    private static IQueryable<Country> Filter(IQueryable<Country> source, string code)
    {
        var value = code?.Trim() ?? "";
        var upper = value.ToUpperInvariant();
        var exact = EscapeLike(value);

        return value.Length switch
        {
            2 => source.Where(c => c.ISO2 == upper),
            3 => source.Where(c => c.ISO3 == upper),
            _ => source.Where(c => EF.Functions.Like(c.Name, exact, LikeEscape) || EF.Functions.Like(c.NameGER, exact, LikeEscape)),
        };
    }

    private async Task<int> ResolveWorldRegionIdAsync(string input, CancellationToken ct)
    {
        var value = input.Trim();

        // Akzeptiert ISO-Kürzel ("EU") und die deutschen Suchbegriffe des Original-$search ("LaenderInEuropa")
        if (WorldRegionAliases.ByTerm.TryGetValue(value, out var aliasId))
            return aliasId;

        var upper = value.ToUpperInvariant();
        var id = await db.WorldRegions.AsNoTracking()
            .Where(w => w.ISO == upper)
            .Select(w => (int?)w.Id)
            .FirstOrDefaultAsync(ct);
        if (id is not null)
            return id.Value;

        var valid = await db.WorldRegions.AsNoTracking().OrderBy(w => w.ISO).Select(w => w.ISO).ToListAsync(ct);
        throw new AgentInputException($"Unbekannte Weltregion '{input}'. Gültig: {string.Join(", ", valid)}.");
    }

    private static IQueryable<CountryInfo> Project(IQueryable<Country> query) =>
        query.Select(c => new CountryInfo(
            c.Id, c.ISO2, c.ISO3, c.Name, c.DisplayNameGER,
            c.EU == "Y" || c.EU == "J",
            c.EWR == "Y" || c.EWR == "J",
            c.WorldRegion != null ? c.WorldRegion.Name : null,
            c.CountryRegion != null ? c.CountryRegion.Name : null));

    private static string ValidateName(string value, string field)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is 0 or > MaxNameLength)
            throw new AgentInputException($"'{field}' muss 1 bis {MaxNameLength} Zeichen lang sein.");
        return trimmed;
    }

    private static AgentInputException NotFound(string code) =>
        new($"Kein Land zu '{code}' gefunden. Verwende einen ISO2-Code (z. B. 'AT'), ISO3-Code (z. B. 'AUT') oder den exakten Namen.");

    /// <summary>Neutralisiert die LIKE-Platzhalter, damit Eingaben nie als Muster wirken.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}
