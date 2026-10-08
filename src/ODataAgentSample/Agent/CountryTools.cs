using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ODataAgentSample.Security;

namespace ODataAgentSample.Agent;

/// <summary>
/// Die Tools, die ein KI-Agent über MCP sieht. Sie ersetzen den freien OData-Zugriff durch wenige,
/// gut beschriebene Operationen: das Schema der Tools ist die Dokumentation für das Modell.
/// </summary>
[McpServerToolType]
public sealed partial class CountryTools(CountryQueryService countries, IHttpContextAccessor httpContext, ILogger<CountryTools> logger)
{
    private string Client => httpContext.HttpContext?.User.Identity?.Name ?? "unbekannt";

    [McpServerTool(Name = "search_countries", ReadOnly = true, Idempotent = true)]
    [Description("Sucht LÄnder (Stand der Seed-Daten: 264). Alle Filter sind optional und werden mit UND kombiniert. " +
                 "Ergebnis ist seitenweise (limit/offset); 'total' ist die Gesamtzahl der Treffer, 'hasMore' zeigt weitere Seiten an. " +
                 "Entspricht in OData: $filter + $orderby + $top/$skip + $count.")]
    public Task<CountrySearchResult> SearchCountries(
        [Description("Teil eines Namens (deutsch oder englisch) oder ISO-Codes, ohne Beachtung der Groß-/Kleinschreibung.")] string? text = null,
        [Description("Weltregion als ISO-Kürzel: AF, SA, NA, EU, OC, AS (siehe list_regions).")] string? worldRegion = null,
        [Description("true = nur EU-Mitglieder, false = nur Nicht-Mitglieder.")] bool? euMember = null,
        [Description("true = nur EWR-Mitglieder, false = nur Nicht-Mitglieder.")] bool? ewrMember = null,
        [Description("Maximale Trefferzahl pro Seite, 1 bis 100.")] int limit = CountryQueryService.DefaultLimit,
        [Description("Anzahl zu überspringender Treffer für die nÄchste Seite.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        LogCall("search_countries", $"text={text} region={worldRegion} eu={euMember} ewr={ewrMember} limit={limit} offset={offset}");
        return Run(() => countries.SearchAsync(text, worldRegion, euMember, ewrMember, limit, offset, cancellationToken));
    }

    [McpServerTool(Name = "get_country", ReadOnly = true, Idempotent = true)]
    [Description("Liefert ein einzelnes Land inklusive Weltregion und LÄnderregion. " +
                 "Entspricht in OData: Countries(key)?$expand=WorldRegion,CountryRegion, nur eben über den ISO-Code statt über die interne Id.")]
    public Task<CountryInfo> GetCountry(
        [Description("ISO2-Code (AT), ISO3-Code (AUT) oder exakter Name (Österreich / Austria).")] string code,
        CancellationToken cancellationToken = default)
    {
        LogCall("get_country", code);
        return Run(() => countries.GetAsync(code, cancellationToken));
    }

    [McpServerTool(Name = "list_regions", ReadOnly = true, Idempotent = true)]
    [Description("Listet alle Weltregionen mit LÄnderzahl, Zahl der EU-Mitglieder und Unterregionen. " +
                 "Das ist der beste erste Schritt, um die Daten zu verstehen. " +
                 "Entspricht in OData: Countries?$apply=groupby((WorldRegionId),aggregate($count as CountryCount)).")]
    public Task<IReadOnlyList<WorldRegionInfo>> ListRegions(CancellationToken cancellationToken = default)
    {
        LogCall("list_regions", null);
        return Run(() => countries.ListRegionsAsync(cancellationToken));
    }

    [McpServerTool(Name = "update_country", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("ÄNDERT ein Land. Nur mit API-Key der Rolle 'writer'. Nur die angegebenen Felder werden geÄndert. " +
                 "Frage vor dem Aufruf im Zweifel den Benutzer. Entspricht in OData: PATCH Countries(key).")]
    public Task<CountryInfo> UpdateCountry(
        [Description("ISO2-Code, ISO3-Code oder exakter Name des Landes.")] string code,
        [Description("Neuer deutscher Anzeigename.")] string? displayNameGer = null,
        [Description("EU-Mitgliedschaft setzen.")] bool? euMember = null,
        [Description("EWR-Mitgliedschaft setzen.")] bool? ewrMember = null,
        CancellationToken cancellationToken = default)
    {
        LogCall("update_country", $"code={code} displayNameGer={displayNameGer} eu={euMember} ewr={ewrMember}");

        // Zweite Absicherung neben dem Key-Handler: Schreibrechte werden pro Tool-Aufruf geprüft
        if (httpContext.HttpContext?.User.IsInRole(Roles.Writer) != true)
            throw new McpException("Keine Berechtigung: update_country erfordert einen API-Key mit der Rolle 'writer'.");

        return Run(() => countries.UpdateAsync(code, displayNameGer, euMember, ewrMember, cancellationToken));
    }

    /// <summary>
    /// Fachliche Eingabefehler gehen als McpException mit Klartext an den Agenten zurück, damit er sich korrigieren kann.
    /// Alle anderen Fehler bleiben intern (das SDK meldet dann nur generisch).
    /// </summary>
    private static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (AgentInputException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    /// <summary>Audit-Log: wer hat welches Tool mit welchen Argumenten aufgerufen (auf 300 Zeichen gekürzt).</summary>
    private void LogCall(string tool, string? arguments)
    {
        if (!logger.IsEnabled(LogLevel.Information))
            return;

        var shortened = arguments is { Length: > 300 } ? arguments[..300] + "..." : arguments;
        LogAudit(Client, tool, shortened);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {Client} ruft {Tool} auf: {Arguments}")]
    private partial void LogAudit(string client, string tool, string? arguments);
}
