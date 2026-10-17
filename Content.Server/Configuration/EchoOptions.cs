namespace Content.Server.Configuration;

public sealed class EchoOptions
{
    public const string SectionName = "Echoes";
    public bool Enabled { get; set; }
    public string DefinitionId { get; set; } = "test_guardian_echo";
    public float DecisionSeconds { get; set; } = .2f;
}
