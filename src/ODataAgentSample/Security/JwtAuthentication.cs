using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ODataAgentSample.Security;

/// <summary>Schema-Namen und die Auswahl zwischen API-Key und JWT.</summary>
public static class AuthSchemes
{
    /// <summary>Policy-Schema: entscheidet pro Anfrage, welcher Handler zuständig ist.</summary>
    public const string Smart = "ApiKeyOrJwt";

    /// <summary>Ein "Authorization: Bearer"-Wert in JWT-Form (drei Base64url-Teile) geht an JwtBearer, alles andere an den API-Key-Handler.</summary>
    public static string Select(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && LooksLikeJwt(header[prefix.Length..].Trim()))
            return JwtBearerDefaults.AuthenticationScheme;

        return ApiKeyDefaults.Scheme;
    }

    public static bool LooksLikeJwt(string token)
    {
        // Die Signatur darf leer sein ("a.b."): so ein unsigniertes Token geht an JwtBearer und wird dort abgelehnt,
        // statt als API-Key durchzurutschen.
        var parts = token.Split('.');
        return parts.Length == 3 && parts[0].Length > 0 && parts[1].Length > 0;
    }
}

/// <summary>JwtBearer-Einstellungen und die Abbildung von Token-Claims auf die Rollen <c>reader</c> und <c>writer</c>.</summary>
public static class JwtBearerSetup
{
    public static void Configure(JwtBearerOptions options, JwtOptions jwt)
    {
        options.MapInboundClaims = false;          // Claim-Namen wie im Token lassen (scp, roles, sub)
        options.SaveToken = false;

        var parameters = options.TokenValidationParameters;
        parameters.ValidateAudience = true;
        parameters.ValidAudience = jwt.Audience;
        parameters.ValidateIssuer = true;
        parameters.ValidateLifetime = true;
        parameters.RequireExpirationTime = true;
        parameters.RequireSignedTokens = true;     // unsignierte Tokens ("alg": "none") werden abgelehnt
        parameters.ClockSkew = TimeSpan.FromSeconds(30);
        parameters.NameClaimType = ClaimTypes.Name;
        parameters.RoleClaimType = ClaimTypes.Role;

        if (jwt.HasAuthority)
        {
            options.Authority = jwt.Authority;
            options.RequireHttpsMetadata = !(Uri.TryCreate(jwt.Authority, UriKind.Absolute, out var uri) && uri.Scheme == "http");
            if (!string.IsNullOrWhiteSpace(jwt.Issuer))
                parameters.ValidIssuer = jwt.Issuer;
        }
        else
        {
            parameters.ValidIssuer = jwt.Issuer;
            parameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey!));
            parameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];   // verhindert Algorithmus-Verwechslung
        }

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                MapClaims(context.Principal, jwt);
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>
    /// Scopes (<c>scp</c> oder <c>scope</c>, getrennt durch Leerzeichen) und App-Rollen (<c>roles</c>) werden zu den Rollen
    /// <c>reader</c>/<c>writer</c>. Schreiben schließt Lesen ein. Ein Token ohne beides bleibt ohne Rolle und bekommt 403.
    /// </summary>
    public static void MapClaims(ClaimsPrincipal? principal, JwtOptions jwt)
    {
        if (principal?.Identities.FirstOrDefault() is not { } identity)
            return;

        var granted = identity.FindAll("scp").Concat(identity.FindAll("scope"))
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Concat(identity.FindAll("roles").Select(c => c.Value))
            .ToHashSet(StringComparer.Ordinal);

        var canWrite = granted.Contains(jwt.WriterScope) || granted.Contains(Roles.Writer);
        var canRead = canWrite || granted.Contains(jwt.ReaderScope) || granted.Contains(Roles.Reader);

        if (canRead) identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Reader));
        if (canWrite) identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Writer));

        // Anzeigename für Audit-Log und Rate Limit: bevorzugt preferred_username, sonst sub
        var name = identity.FindFirst("preferred_username")?.Value ?? identity.FindFirst("sub")?.Value;
        if (name is not null && identity.FindFirst(ClaimTypes.Name) is null)
            identity.AddClaim(new Claim(ClaimTypes.Name, name));
    }
}

/// <summary>
/// Stellt lokale Demo-Tokens aus (nur mit <see cref="JwtOptions.SigningKey"/>). In Produktion stellt der Identity Provider
/// (z. B. Entra ID) die Tokens aus; diese Klasse wird dort nicht benutzt.
/// </summary>
public static class JwtTokenIssuer
{
    public static string Issue(
        JwtOptions options,
        string subject,
        IEnumerable<string>? scopes = null,
        IEnumerable<string>? roles = null,
        TimeSpan? lifetime = null,
        DateTimeOffset? now = null)
    {
        if (!options.HasSigningKey)
            throw new InvalidOperationException("Lokale Tokens gibt es nur mit Authentication:Jwt:SigningKey. Mit Authority stellt der Identity Provider die Tokens aus.");

        var issuedAt = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
        var claims = new List<Claim> { new("sub", subject), new("preferred_username", subject) };
        var scopeText = string.Join(' ', scopes ?? []);
        if (scopeText.Length > 0)
            claims.Add(new Claim("scope", scopeText));
        claims.AddRange((roles ?? []).Select(r => new Claim("roles", r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt + (lifetime ?? TimeSpan.FromHours(1)),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey!)), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
