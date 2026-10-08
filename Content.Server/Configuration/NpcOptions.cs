namespace Content.Server.Configuration;

/// <summary>Temporary single-monster arena data; not permanent death or threat rules.</summary>
public sealed class NpcOptions
{
    public const string SectionName = "Npc";
    public bool Enabled { get; init; }
    public string DefinitionId { get; init; } = "test_creature";
    public float X { get; init; } = 7;
    public float Z { get; init; } = 3;
    public float Speed { get; init; } = 3;
    public float AggroRadius { get; init; } = 6;
    public float LeashRadius { get; init; } = 10;
    public float DecisionSeconds { get; init; } = 0.2f;
    public float WindupSeconds { get; init; } = 1;
}
