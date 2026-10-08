using System.Reflection;
using System.Text.Json;
using ODataAgentSample.Domain;

namespace ODataAgentSample.Infrastructure.Db;

/// <summary>Befuellt die Datenbank aus den eingebetteten JSON-Dateien des ODataSample (data/*.json).</summary>
public static class DatabaseSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task ResetAndSeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);

        // Reihenfolge wegen der Fremdschluessel: Welt-Regionen, Laender-Regionen, Laender
        db.WorldRegions.AddRange(Load<WorldRegion>("WorldRegions.json"));
        db.CountryRegions.AddRange(Load<CountryRegion>("CountryRegions.json"));
        db.Countries.AddRange(Load<Country>("Countries.json"));
        await db.SaveChangesAsync(ct);
    }

    private static List<T> Load<T>(string file)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"Data.{file}")
            ?? throw new InvalidOperationException($"Eingebettete Ressource 'Data.{file}' fehlt.");
        return JsonSerializer.Deserialize<List<T>>(stream, JsonOptions) ?? [];
    }
}
