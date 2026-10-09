using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using ODataAgentSample.Security;

namespace ODataAgentSample.Tests.Support;

/// <summary>Startet die echte Anwendung im Speicher mit eigener, frisch gesaeter SQLite-Datei und zwei Test-Keys.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string ReaderKey = "test-reader-key-0123456789";
    public const string WriterKey = "test-writer-key-0123456789";

    public const string JwtIssuer = "test-issuer";
    public const string JwtAudience = "test-audience";
    public const string JwtSigningKey = "test-signing-key-0123456789-0123456789-0123456789-0123456789-abcdefghij";

    /// <summary>Dieselben Werte, mit denen der Server startet. Damit stellen die Tests gültige und gezielt kaputte Tokens aus.</summary>
    public static JwtOptions JwtSettings => new() { Issuer = JwtIssuer, Audience = JwtAudience, SigningKey = JwtSigningKey };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"odata-agent-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
        builder.UseSetting("Authentication:ApiKeys:0:Name", "test-reader");
        builder.UseSetting("Authentication:ApiKeys:0:KeySha256", ApiKeyHasher.Hash(ReaderKey));
        builder.UseSetting("Authentication:ApiKeys:0:Role", Roles.Reader);
        builder.UseSetting("Authentication:ApiKeys:1:Name", "test-writer");
        builder.UseSetting("Authentication:ApiKeys:1:KeySha256", ApiKeyHasher.Hash(WriterKey));
        builder.UseSetting("Authentication:ApiKeys:1:Role", Roles.Writer);
        builder.UseSetting("Authentication:Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Authentication:Jwt:Audience", JwtAudience);
        builder.UseSetting("Authentication:Jwt:SigningKey", JwtSigningKey);
    }

    public HttpClient CreateClientWithKey(string key)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, key);
        return client;
    }

    public HttpClient CreateClientWithBearer(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateReaderClient() => CreateClientWithKey(ReaderKey);

    public HttpClient CreateWriterClient() => CreateClientWithKey(WriterKey);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
        }
    }
}
