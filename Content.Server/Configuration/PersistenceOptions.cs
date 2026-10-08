namespace Content.Server.Configuration;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";
    public string Provider { get; set; } = "Sqlite";
    public string SqlitePath { get; set; } = ".data/project-g-development.db";
    public string ConnectionString { get; set; } = "";
    public int MaxSessions { get; set; } = 32;
}
