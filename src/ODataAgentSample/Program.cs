using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

// Hilfsbefehl: lokales Demo-Token ausstellen (nur mit Authentication:Jwt:SigningKey, z. B. im Development-Profil)
//   dotnet run --project src/ODataAgentSample -- issue-token <subject> <scope> [<scope> ...]
if (args is ["issue-token", var subject, .. var scopes])
{
    // Wie die Anwendung selbst: Die Umgebung bestimmt, welche appsettings.<Umgebung>.json gilt. Ohne Angabe ist es
    // Production, dort gibt es keinen Demo-Schlüssel. (dotnet run mit dem Standard-Startprofil setzt Development.)
    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                      ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                      ?? "Production";
    var settings = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{environment}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    var options = settings.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    var problem = options.Validate();
    if (problem is not null || !options.HasSigningKey)
    {
        Console.Error.WriteLine(problem ?? "Kein Authentication:Jwt:SigningKey konfiguriert. Lokale Tokens gibt es nur damit.");
        return;
    }

    Console.WriteLine(JwtTokenIssuer.Issue(options, subject, scopes));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// --- Konfiguration (Fehler fallen beim Start auf, nicht erst beim ersten Request) ---
builder.Services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

// OAuth2/JWT ist optional. Ist der Abschnitt Authentication:Jwt gesetzt, muss er vollständig und gültig sein (Start schlägt sonst fehl).
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddOptions<JwtOptions>().Bind(builder.Configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

builder.Services.AddOptions<ApiKeyOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyOptions.SectionName))
    .Validate(o => o.ApiKeys.Count > 0 || jwt.IsEnabled,
        "Es ist keine Anmeldeart konfiguriert (Authentication:ApiKeys oder Authentication:Jwt). Der Server startet nicht ungeschuetzt.")
    .Validate(o => o.ApiKeys.All(k => k.Name.Length > 0 && ApiKeyHasher.TryParseHash(k.KeySha256, out _)
                                      && k.Role is Roles.Reader or Roles.Writer),
        "Jeder API-Key braucht Name, SHA-256-Hash (64 Hex-Zeichen) und Rolle 'reader' oder 'writer'.")
    .ValidateOnStart();

// --- Authentifizierung & Autorisierung: standardmaessig ist alles geschuetzt, Aenderungen nur mit Rolle 'writer' ---
// Zwei Anmeldearten: API-Key (immer) und OAuth2/JWT (wenn konfiguriert). Ein Policy-Schema wählt pro Anfrage den Handler.
var authentication = builder.Services.AddAuthentication(jwt.IsEnabled ? AuthSchemes.Smart : ApiKeyDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, _ => { });
if (jwt.IsEnabled)
{
    authentication
        .AddPolicyScheme(AuthSchemes.Smart, "API-Key oder JWT", o => o.ForwardDefaultSelector = AuthSchemes.Select)
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o => JwtBearerSetup.Configure(o, jwt));
}

// Fallback: jeder Endpunkt verlangt eine Anmeldung UND mindestens die Rolle reader. Ein gültiges Token ohne passenden
// Scope ist angemeldet, aber nicht berechtigt (403). Schreiben verlangt zusätzlich writer.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(Roles.Reader, Roles.Writer).Build())
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
