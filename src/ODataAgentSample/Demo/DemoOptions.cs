using Microsoft.Extensions.Options;
using ODataAgentSample.Security;

namespace ODataAgentSample.Demo;

/// <summary>
/// Einstellungen der oeffentlichen Demo (Landingpage, Anmeldung mit gemeinsamem Zugangscode, Chat mit Claude).
/// Standardmaessig aus. Eingeschaltet muessen alle Geheimnisse gesetzt sein, sonst startet die App nicht.
/// </summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";
    public const int MinSecretLength = 32;

    public bool Enabled { get; set; }

    /// <summary>SHA-256 (64 Hex-Zeichen) des Zugangscodes, den der Betreiber an Eingeladene gibt.</summary>
    public string AccessCodeSha256 { get; set; } = "";

    /// <summary>Schluessel (mind. 32 Zeichen) zum Signieren des Sitzungs-Cookies. Bleibt ueber Neustarts gleich, damit Anmeldungen halten.</summary>
    public string CookieKey { get; set; } = "";

    /// <summary>Klartext des Reader-Keys, mit dem die Demo ihren eigenen MCP-Endpunkt aufruft. Wird als Reader registriert, nie als Writer.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>API-Key fuer Anthropic. Bleibt auf dem Server, nie im Browser, nie im Repo.</summary>
    public string AnthropicApiKey { get; set; } = "";

    public string Model { get; set; } = "claude-haiku-5-5";

    public int HourLimit { get; set; } = 100;
    public int SessionHourLimit { get; set; } = 15;
    public int DayLimit { get; set; } = 400;
    public int MaxPromptLength { get; set; } = 300;
    public int MaxToolRounds { get; set; } = 6;
    public int MaxOutputTokens { get; set; } = 1024;
    public int RunTimeoutSeconds { get; set; } = 60;
    public int SessionHours { get; set; } = 8;

    /// <summary>Optional. Standard: der eigene Port der App. Nur Loopback ist erlaubt (kein Weiterleiten an fremde Ziele).</summary>
    public string? McpUrl { get; set; }

    public string? Validate()
    {
        if (!Enabled)
            return null;

        if (!ApiKeyHasher.TryParseHash(AccessCodeSha256, out _))
            return "Demo:AccessCodeSha256 muss ein SHA-256 als 64 Hex-Zeichen sein.";
        if (CookieKey.Length < MinSecretLength)
            return $"Demo:CookieKey ist zu kurz (mindestens {MinSecretLength} Zeichen).";
        if (ApiKey.Length < MinSecretLength)
            return $"Demo:ApiKey ist zu kurz (mindestens {MinSecretLength} Zeichen).";
        if (string.IsNullOrWhiteSpace(AnthropicApiKey))
            return "Demo:AnthropicApiKey fehlt.";
        if (string.IsNullOrWhiteSpace(Model))
            return "Demo:Model fehlt.";
        if (HourLimit < 1 || SessionHourLimit < 1 || DayLimit < 1)
            return "Demo: Die Limits (HourLimit, SessionHourLimit, DayLimit) müssen mindestens 1 sein.";
        if (MaxPromptLength is < 1 or > 2000)
            return "Demo:MaxPromptLength muss zwischen 1 und 2000 liegen.";
        if (MaxToolRounds is < 1 or > 10)
            return "Demo:MaxToolRounds muss zwischen 1 und 10 liegen.";
        if (MaxOutputTokens is < 64 or > 4096)
            return "Demo:MaxOutputTokens muss zwischen 64 und 4096 liegen.";
        if (RunTimeoutSeconds is < 5 or > 300)
            return "Demo:RunTimeoutSeconds muss zwischen 5 und 300 liegen.";
        if (SessionHours is < 1 or > 72)
            return "Demo:SessionHours muss zwischen 1 und 72 liegen.";

        if (McpUrl is not null)
        {
            if (!Uri.TryCreate(McpUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)
                return "Demo:McpUrl muss eine absolute http-Adresse auf localhost sein (kein Weiterleiten an fremde Ziele).";
        }

        return null;
    }
}

/// <summary>Meldet Konfigurationsfehler beim Start mit Klartext statt erst beim ersten Request.</summary>
public sealed class DemoOptionsValidator : IValidateOptions<DemoOptions>
{
    public ValidateOptionsResult Validate(string? name, DemoOptions options) =>
        options.Validate() is { } error ? ValidateOptionsResult.Fail(error) : ValidateOptionsResult.Success;
}
