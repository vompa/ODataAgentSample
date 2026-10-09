using System.Text;
using Microsoft.Extensions.Options;

namespace ODataAgentSample.Security;

/// <summary>
/// Konfiguration der OAuth2/JWT-Anmeldung unter <c>Authentication:Jwt</c>. Ohne diesen Abschnitt bleibt es bei API-Keys.
/// Produktion: <see cref="Authority"/> (OIDC-Discovery, z. B. Microsoft Entra ID) und <see cref="Audience"/>.
/// Lokale Demo und Tests: <see cref="SigningKey"/> mit <see cref="Issuer"/> (symmetrisch, nie in Produktion verwenden).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";
    public const int MinSigningKeyBytes = 32;

    /// <summary>Aussteller-URL mit OIDC-Discovery, z. B. https://login.microsoftonline.com/{tenant}/v2.0.</summary>
    public string? Authority { get; set; }

    /// <summary>Für wen das Token ausgestellt sein muss (Claim "aud"), z. B. api://odata-agent-sample.</summary>
    public string? Audience { get; set; }

    /// <summary>Erwarteter Aussteller (Claim "iss"). Pflicht beim <see cref="SigningKey"/>, sonst optional.</summary>
    public string? Issuer { get; set; }

    /// <summary>Symmetrischer HS256-Schlüssel (mindestens 32 Bytes) für lokale Demo-Tokens. Schließt <see cref="Authority"/> aus.</summary>
    public string? SigningKey { get; set; }

    /// <summary>Scope, der Lesezugriff gewährt (Entra ID: Claim "scp"; sonst "scope").</summary>
    public string ReaderScope { get; set; } = "countries.read";

    /// <summary>Scope, der Schreibzugriff gewährt. Schreiben schließt Lesen ein.</summary>
    public string WriterScope { get; set; } = "countries.write";

    public bool HasAuthority => !string.IsNullOrWhiteSpace(Authority);

    public bool HasSigningKey => !string.IsNullOrWhiteSpace(SigningKey);

    /// <summary>true, sobald irgendein Wert des Abschnitts gesetzt ist (dann muss er auch vollständig und gültig sein).</summary>
    public bool IsConfigured => HasAuthority || HasSigningKey
        || !string.IsNullOrWhiteSpace(Audience) || !string.IsNullOrWhiteSpace(Issuer);

    /// <summary>true, wenn die JWT-Anmeldung aktiv ist.</summary>
    public bool IsEnabled => IsConfigured && Validate() is null;

    /// <summary>Liefert eine Fehlermeldung oder null, wenn die Konfiguration gültig (oder gar nicht vorhanden) ist.</summary>
    public string? Validate()
    {
        if (!IsConfigured)
            return null;

        if (string.IsNullOrWhiteSpace(Audience))
            return "Authentication:Jwt:Audience fehlt. Ein Token muss für diese API ausgestellt sein.";

        if (HasAuthority == HasSigningKey)
            return "Authentication:Jwt braucht genau eines von Authority (Produktion) oder SigningKey (nur lokale Demo und Tests).";

        if (HasAuthority)
        {
            if (!Uri.TryCreate(Authority, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                return "Authentication:Jwt:Authority muss eine absolute URL sein.";
            if (uri.Scheme == "http" && !uri.IsLoopback)
                return "Authentication:Jwt:Authority muss HTTPS verwenden (HTTP nur für localhost).";
        }
        else
        {
            if (Encoding.UTF8.GetByteCount(SigningKey!) < MinSigningKeyBytes)
                return $"Authentication:Jwt:SigningKey ist zu kurz (mindestens {MinSigningKeyBytes} Bytes).";
            if (string.IsNullOrWhiteSpace(Issuer))
                return "Authentication:Jwt:Issuer fehlt (Pflicht zusammen mit SigningKey).";
        }

        if (string.IsNullOrWhiteSpace(ReaderScope) || string.IsNullOrWhiteSpace(WriterScope)
            || string.Equals(ReaderScope, WriterScope, StringComparison.Ordinal))
            return "Authentication:Jwt:ReaderScope und WriterScope müssen gesetzt und verschieden sein.";

        return null;
    }
}

/// <summary>Meldet Konfigurationsfehler beim Start mit Klartext statt erst beim ersten Request.</summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options) =>
        options.Validate() is { } error ? ValidateOptionsResult.Fail(error) : ValidateOptionsResult.Success;
}
