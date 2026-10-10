namespace Content.Server.Configuration;

/// <summary>Temporary training-arena setup, not permanent PvP or death policy.</summary>
public sealed class CombatOptions
{
    public const string SectionName = "Combat";
    public string PlayerDefinitionId { get; set; } = "test_adventurer";
    public string TargetDefinitionId { get; set; } = "test_creature";
    public float TargetX { get; set; } = -7f;
    public float TargetHeight { get; init; }
    public float TargetZ { get; set; } = 3f;
    public float HalfAngleDegrees { get; set; } = 45f;
    public double CriticalMultiplier { get; set; } = 1.5;
    public int MaxAbilityEffects { get; set; } = 128;
    public int MaxCompensationMilliseconds { get; set; } = 100;
    public float MaxAbilityLifetimeSeconds { get; set; } = 10;
    public float ImpactSeconds { get; set; } = 0.15f;
    public double ManaRecoveryIntervalSeconds { get; set; } = 1; // coalesce durable updates.
    public double HealthRecoveryIntervalSeconds { get; set; } = 1;
}
