namespace Content.Server.Configuration;

/// <summary>Separate fixed boss encounter; no participant-count scaling.</summary>
public sealed class BossOptions
{
    public const string SectionName = "Boss";
    public NpcOptions Actor { get; init; } = new()
    {
        DefinitionId = "test_boss", X = 7, Z = -8, Speed = 2.5f, AggroRadius = 5, LeashRadius = 8, WindupSeconds = 1.2f
    };
    public string AreaAbilityId { get; init; } = "boss_ground_area";
    public float ImpactSeconds { get; init; } = 0.2f;
}
