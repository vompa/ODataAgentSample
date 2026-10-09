using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using ODataAgentSample.Security;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.Security;

public class JwtOptionsTests
{
    private const string LongKey = "ein-signing-key-mit-mehr-als-zweiunddreissig-bytes-0123456789";

    private static JwtOptions Valid() => new() { Issuer = "iss", Audience = "aud", SigningKey = LongKey };

    // ---------- Konfiguration ----------

    [Fact]
    public void Empty_configuration_means_jwt_is_off_and_valid()
    {
        var options = new JwtOptions();

        Assert.False(options.IsConfigured);
        Assert.False(options.IsEnabled);
        Assert.Null(options.Validate());
    }

    [Fact]
    public void Signing_key_setup_is_valid_and_enabled()
    {
        Assert.Null(Valid().Validate());
        Assert.True(Valid().IsEnabled);
    }

    [Fact]
    public void Authority_setup_is_valid_and_enabled()
    {
        var options = new JwtOptions { Authority = "https://login.microsoftonline.com/tenant/v2.0", Audience = "api://odata-agent-sample" };

        Assert.Null(options.Validate());
        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void Plain_http_authority_is_only_allowed_for_loopback()
    {
        Assert.Null(new JwtOptions { Authority = "http://localhost:8080/realms/demo", Audience = "aud" }.Validate());
        Assert.NotNull(new JwtOptions { Authority = "http://idp.example.org/realms/demo", Audience = "aud" }.Validate());
    }

    [Theory]
    [InlineData("missing-audience")]
    [InlineData("authority-and-key")]
    [InlineData("neither-authority-nor-key")]
    [InlineData("short-key")]
    [InlineData("key-without-issuer")]
    [InlineData("authority-not-a-url")]
    [InlineData("same-scopes")]
    public void Incomplete_or_unsafe_configuration_is_rejected_and_not_enabled(string problem)
    {
        var options = problem switch
        {
            "missing-audience" => new JwtOptions { Issuer = "iss", SigningKey = LongKey },
            "authority-and-key" => new JwtOptions { Authority = "https://idp.example.org", Audience = "aud", SigningKey = LongKey, Issuer = "iss" },
            "neither-authority-nor-key" => new JwtOptions { Audience = "aud", Issuer = "iss" },
            "short-key" => new JwtOptions { Audience = "aud", Issuer = "iss", SigningKey = "zu-kurz" },
            "key-without-issuer" => new JwtOptions { Audience = "aud", SigningKey = LongKey },
            "authority-not-a-url" => new JwtOptions { Authority = "kein-url", Audience = "aud" },
            "same-scopes" => new JwtOptions { Issuer = "iss", Audience = "aud", SigningKey = LongKey, ReaderScope = "x", WriterScope = "x" },
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        Assert.NotNull(options.Validate());
        Assert.False(options.IsEnabled);
        Assert.False(new JwtOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Server_refuses_to_start_with_a_broken_jwt_section()
    {
        using var factory = new AppFactory().WithWebHostBuilder(b => b.UseSetting("Authentication:Jwt:SigningKey", "zu-kurz"));

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("zu kurz", Flatten(ex), StringComparison.Ordinal);
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            parts.Add(current.Message);
            if (current.InnerException is null) break;
        }

        return string.Join(" | ", parts);
    }

    // ---------- Token-Aussteller ----------

    [Fact]
    public void Issuer_refuses_to_issue_tokens_without_a_signing_key()
    {
        var options = new JwtOptions { Authority = "https://idp.example.org", Audience = "aud" };

        Assert.Throws<InvalidOperationException>(() => JwtTokenIssuer.Issue(options, "me", ["countries.read"]));
    }

    [Fact]
    public void Issued_token_carries_the_expected_claims_and_lifetime()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(
            JwtTokenIssuer.Issue(Valid(), "agent-7", ["countries.read", "countries.write"], ["writer"], TimeSpan.FromMinutes(10), now));

        Assert.Equal("iss", token.Issuer);
        Assert.Contains("aud", token.Audiences);
        Assert.Equal("agent-7", token.Subject);
        Assert.Equal("countries.read countries.write", token.GetClaim("scope").Value);
        Assert.Equal(now.UtcDateTime, token.ValidFrom);
        Assert.Equal(now.UtcDateTime.AddMinutes(10), token.ValidTo);
        Assert.Equal("HS256", token.Alg);
    }

    // ---------- Abbildung von Claims auf Rollen ----------

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void Read_scope_grants_only_reader()
    {
        var principal = Principal(new Claim("scope", "countries.read"));

        JwtBearerSetup.MapClaims(principal, new JwtOptions());

        Assert.True(principal.IsInRole(Roles.Reader));
        Assert.False(principal.IsInRole(Roles.Writer));
    }

    [Fact]
    public void Write_scope_grants_writer_and_reader()
    {
        var principal = Principal(new Claim("scp", "countries.write"));

        JwtBearerSetup.MapClaims(principal, new JwtOptions());

        Assert.True(principal.IsInRole(Roles.Writer));
        Assert.True(principal.IsInRole(Roles.Reader));
    }

    [Fact]
    public void Custom_scope_names_from_the_configuration_are_used()
    {
        var principal = Principal(new Claim("scope", "api.write"));

        JwtBearerSetup.MapClaims(principal, new JwtOptions { ReaderScope = "api.read", WriterScope = "api.write" });

        Assert.True(principal.IsInRole(Roles.Writer));
    }

    [Fact]
    public void Unrelated_scopes_grant_no_role_at_all()
    {
        var principal = Principal(new Claim("scope", "openid profile email"));

        JwtBearerSetup.MapClaims(principal, new JwtOptions());

        Assert.False(principal.IsInRole(Roles.Reader));
        Assert.False(principal.IsInRole(Roles.Writer));
    }

    [Fact]
    public void Scope_names_must_match_exactly()
    {
        var principal = Principal(new Claim("scope", "countries.writer xcountries.write"));

        JwtBearerSetup.MapClaims(principal, new JwtOptions());

        Assert.False(principal.IsInRole(Roles.Writer));
    }

    [Fact]
    public void Display_name_prefers_preferred_username_over_subject()
    {
        var principal = Principal(new Claim("sub", "abc-123"), new Claim("preferred_username", "agent.smith"), new Claim("scope", "countries.read"));

        JwtBearerSetup.MapClaims(principal, new JwtOptions());

        Assert.Equal("agent.smith", principal.Identity!.Name);
    }

    [Fact]
    public void Display_name_falls_back_to_subject()
    {
        var principal = Principal(new Claim("sub", "abc-123"), new Claim("scope", "countries.read"));

        JwtBearerSetup.MapClaims(principal, new JwtOptions());

        Assert.Equal("abc-123", principal.Identity!.Name);
    }

    // ---------- Auswahl zwischen API-Key und JWT ----------

    private static string Select(string? authorization, string? apiKeyHeader = null)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        if (apiKeyHeader is not null) context.Request.Headers["X-Api-Key"] = apiKeyHeader;
        return AuthSchemes.Select(context);
    }

    [Theory]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXI", "Bearer")]
    [InlineData("bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXI", "Bearer")]
    [InlineData("Bearer eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.", "Bearer")]            // leere Signatur: JwtBearer lehnt ab
    [InlineData("Bearer test-reader-key-0123456789", "ApiKey")]
    [InlineData("Bearer a.b", "ApiKey")]
    [InlineData("Basic dXNlcjpwYXNz", "ApiKey")]
    [InlineData(null, "ApiKey")]
    public void Scheme_selection_routes_jwt_shaped_bearer_tokens_to_jwt_and_everything_else_to_api_key(string? header, string expected) =>
        Assert.Equal(expected, Select(header));

    [Fact]
    public void X_api_key_header_always_means_api_key() =>
        Assert.Equal("ApiKey", Select(null, "test-reader-key-0123456789"));
}
