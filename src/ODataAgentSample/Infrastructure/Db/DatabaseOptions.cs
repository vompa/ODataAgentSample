namespace ODataAgentSample.Infrastructure.Db;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "Data Source=odata-agent.db";

    /// <summary>Wie im Original-ODataSample: Datenbank beim Start neu aufbauen und aus data/*.json befuellen.</summary>
    public bool ReseedOnStartup { get; set; } = true;
}
