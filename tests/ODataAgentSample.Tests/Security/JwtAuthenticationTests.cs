using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ODataAgentSample.Security;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.Security;

/// <summary>OAuth2/JWT gegen den echten Server: gültige Tokens, Berechtigungen aus Scopes und Rollen, und gezielt kaputte Tokens.</summary>
public sealed class JwtAuthenticationTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string Token(
        string[]? scopes = null, string[]? roles = null, JwtOptions? settings = null,
        DateTimeOffset? now = null, TimeSpan? lifetime = null) =>
        JwtTokenIssuer.Issue(settings ?? AppFactory.JwtSettings, "agent-1", scopes, roles, lifetime, now);

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JwtOptions With(Action<JwtOptions> change)
    {
        var options = AppFactory.JwtSettings;
        change(options);
        return options;
    }

    private async Task<int> AnyCountryIdAsync()
    {
        var json = await factory.CreateReaderClient().GetStringAsync(Url("/odata/v1/Countries?$top=1&$select=Id"));
        return JsonDocument.Parse(json).RootElement.GetProperty("value")[0].GetProperty("Id").GetInt32();
    }

    // ---------- Berechtigungen ----------

    [Fact]
    public async Task Token_with_read_scope_can_read_odata()
    {
        var client = factory.CreateClientWithBearer(Token(scopes: ["countries.read"]));

        var response = await client.GetAsync(Url("/odata/v1/Countries?$top=1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_with_read_scope_cannot_modify()
    {
        var client = factory.CreateClientWithBearer(Token(scopes: ["countries.read"]));
        var id = await AnyCountryIdAsync();

        var response = await client.PatchAsync(Url("/odata/v1/Countries(" + id + ")"), Json("{\"DisplayNameGER\":\"Hacked\"}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_with_write_scope_can_modify_and_implies_read()
    {
        var client = factory.CreateClientWithBearer(Token(scopes: ["countries.write"]));
        var id = await AnyCountryIdAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url("/odata/v1/Countries?$top=1"))).StatusCode);
        var patch = await client.PatchAsync(Url("/odata/v1/Countries(" + id + ")"), Json("{\"DisplayNameGER\":\"Per JWT geändert\"}"));

        Assert.True(patch.IsSuccessStatusCode, patch.StatusCode.ToString());
    }

    [Fact]
    public async Task Several_scopes_in_one_claim_are_understood()
    {
        var client = factory.CreateClientWithBearer(Token(scopes: ["openid", "countries.read", "countries.write"]));
        var id = await AnyCountryIdAsync();

        var patch = await client.PatchAsync(Url("/odata/v1/Countries(" + id + ")"), Json("{\"DisplayNameGER\":\"Mehrere Scopes\"}"));

        Assert.True(patch.IsSuccessStatusCode, patch.StatusCode.ToString());
    }

    [Fact]
    public async Task App_role_claim_works_like_a_scope()
    {
        // Entra ID liefert App-Rollen im Claim "roles", Delegated Permissions in "scp"
        var client = factory.CreateClientWithBearer(Token(roles: ["writer"]));
        var id = await AnyCountryIdAsync();

        var patch = await client.PatchAsync(Url("/odata/v1/Countries(" + id + ")"), Json("{\"DisplayNameGER\":\"Per Rolle\"}"));

        Assert.True(patch.IsSuccessStatusCode, patch.StatusCode.ToString());
    }

    [Theory]
    [InlineData("/odata/v1/Countries?$top=1")]
    [InlineData("/odata/v1/$metadata")]
    public async Task Valid_token_without_any_matching_scope_is_authenticated_but_forbidden(string path)
    {
        var client = factory.CreateClientWithBearer(Token(scopes: ["openid", "profile"]));

        var response = await client.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Gezielt kaputte Tokens ----------

    private static string BadToken(string kind) => kind switch
    {
        "expired" => Token(scopes: ["countries.write"], now: DateTimeOffset.UtcNow.AddHours(-3), lifetime: TimeSpan.FromHours(1)),
        "not-yet-valid" => Token(scopes: ["countries.write"], now: DateTimeOffset.UtcNow.AddHours(2)),
        "wrong-signing-key" => Token(scopes: ["countries.write"],
            settings: With(o => o.SigningKey = "ein-ganz-anderer-schluessel-0123456789-0123456789-0123456789-xyz")),
        "wrong-audience" => Token(scopes: ["countries.write"], settings: With(o => o.Audience = "eine-andere-api")),
        "wrong-issuer" => Token(scopes: ["countries.write"], settings: With(o => o.Issuer = "ein-anderer-aussteller")),
        "other-algorithm" => OtherAlgorithm(),
        "unsigned" => Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." +
                      Base64Url("{\"iss\":\"" + AppFactory.JwtIssuer + "\",\"aud\":\"" + AppFactory.JwtAudience +
                                "\",\"scope\":\"countries.write\",\"exp\":" + DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() + "}") + ".",
        "garbage" => "abc.def.ghi",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Gleicher Schlüssel, aber HS512 statt HS256: der Server erlaubt nur den konfigurierten Algorithmus.</summary>
    private static string OtherAlgorithm()
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = AppFactory.JwtIssuer,
            Audience = AppFactory.JwtAudience,
            Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("scope", "countries.write")]),
            NotBefore = now,
            Expires = now.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AppFactory.JwtSigningKey)), SecurityAlgorithms.HmacSha512),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("not-yet-valid")]
    [InlineData("wrong-signing-key")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("other-algorithm")]
    [InlineData("unsigned")]
    [InlineData("garbage")]
    public async Task Invalid_tokens_are_rejected_with_401(string kind)
    {
        var client = factory.CreateClientWithBearer(BadToken(kind));

        var response = await client.GetAsync(Url("/odata/v1/Countries?$top=1"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-signing-key")]
    [InlineData("unsigned")]
    public async Task Invalid_tokens_cannot_modify_either(string kind)
    {
        var client = factory.CreateClientWithBearer(BadToken(kind));

        var response = await client.PatchAsync(Url("/odata/v1/Countries(1)"), Json("{\"DisplayNameGER\":\"Hacked\"}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Zusammenspiel mit API-Key ----------

    [Fact]
    public async Task Api_keys_still_work_next_to_jwt()
    {
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateReaderClient().GetAsync(Url("/odata/v1/Countries?$top=1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.CreateReaderClient().PatchAsync(Url("/odata/v1/Countries(1)"), Json("{\"DisplayNameGER\":\"x\"}"))).StatusCode);
    }

    [Fact]
    public async Task Api_key_sent_as_bearer_token_still_works()
    {
        var response = await factory.CreateClientWithBearer(AppFactory.ReaderKey).GetAsync(Url("/odata/v1/Countries?$top=1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_stays_public_and_unknown_clients_get_401()
    {
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync(Url("/health"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Url("/odata/v1/Countries"))).StatusCode);
    }

    // ---------- MCP mit JWT ----------

    private async Task<McpClient> ConnectWithJwtAsync(string token)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
            },
            factory.CreateClient(),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    [Fact]
    public async Task Agent_with_a_read_token_can_search_but_not_update()
    {
        await using var client = await ConnectWithJwtAsync(Token(scopes: ["countries.read"]));

        var search = await client.CallToolAsync("search_countries", new Dictionary<string, object?> { ["text"] = "switzerland", ["limit"] = 3 });
        var update = await client.CallToolAsync("update_country", new Dictionary<string, object?> { ["code"] = "LI", ["displayNameGer"] = "Hacked" });

        Assert.NotEqual(true, search.IsError);
        Assert.Contains("CHE", TextOf(search), StringComparison.Ordinal);
        Assert.True(update.IsError);
        Assert.Contains("writer", TextOf(update), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_with_a_write_token_can_update()
    {
        await using var client = await ConnectWithJwtAsync(Token(scopes: ["countries.write"]));

        var update = await client.CallToolAsync("update_country", new Dictionary<string, object?> { ["code"] = "SM", ["displayNameGer"] = "San Marino (JWT)" });

        Assert.NotEqual(true, update.IsError);
        Assert.Contains("San Marino (JWT)", TextOf(update), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_rejects_an_invalid_token_when_connecting() =>
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectWithJwtAsync(BadToken("wrong-signing-key")));
}
