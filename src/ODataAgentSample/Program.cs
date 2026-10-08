using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ODataAgentSample.Agent;
using ODataAgentSample.Infrastructure.Db;
using ODataAgentSample.OData;
using ODataAgentSample.Security;

// Hilfsbefehl: dotnet run --project src/ODataAgentSample -- hash-key "<dein-key>"
if (args is ["hash-key", var plainKey])
{
    Console.WriteLine(ApiKeyHasher.Hash(plainKey));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// --- Konfiguration (Fehler fallen beim Start auf, nicht erst beim ersten Request) ---
builder.Services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.AddOptions<ApiKeyOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyOptions.SectionName))
    .Validate(o => o.ApiKeys.Count > 0, "Es ist kein API-Key konfiguriert (Authentication:ApiKeys). Der Server startet nicht ungeschuetzt.")
    .Validate(o => o.ApiKeys.All(k => k.Name.Length > 0 && ApiKeyHasher.TryParseHash(k.KeySha256, out _)
                                      && k.Role is Roles.Reader or Roles.Writer),
        "Jeder API-Key braucht Name, SHA-256-Hash (64 Hex-Zeichen) und Rolle 'reader' oder 'writer'.")
    .ValidateOnStart();

// --- Authentifizierung & Autorisierung: standardmaessig ist alles geschuetzt, Aenderungen nur mit Rolle 'writer' ---
builder.Services.AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, _ => { });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.Writer, p => p.RequireRole(Roles.Writer));

// --- Rate Limiting pro Client (bzw. pro IP fuer nicht authentifizierte Anfragen, bremst Key-Raten) ---
var requestsPerMinute = builder.Configuration.GetValue("RateLimit:RequestsPerMinute", 120);
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

// --- Daten & OData ---
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

builder.Services.AddScoped<CountryQueryService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<CountryTools>();

builder.Services.AddControllers()
    .AddOData(options =>
    {
        options.AddRouteComponents("odata/v1", CountriesEdmModel.Build(),
            services => services.AddSingleton<ISearchBinder, CountrySearchBinder>());
        options.EnableQueryFeatures();
    });

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ReseedOnStartup)
        await DatabaseSeeder.ResetAndSeedAsync(db);
}

app.UseAuthentication();
app.UseRateLimiter();   // nach Authentication (kennt den Client), vor Authorization (bremst auch fehlgeschlagene Versuche)
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapControllers();
app.MapMcp("/mcp");

app.Run();

public partial class Program;
