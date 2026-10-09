namespace Content.Server.Stats;

/// <summary>Versioned prototype coefficients, not final release balance.</summary>
public sealed record StatBalance
{
    public required StatWeights MeleeAttack { get; init; }
    public required StatWeights RangedAttack { get; init; }
    public required StatWeights MagicAttack { get; init; }
    public required StatWeights MeleeWeapon { get; init; }
    public required StatWeights RangedWeapon { get; init; }
    public required StatWeights PhysicalDefense { get; init; }
    public required StatWeights MagicDefense { get; init; }
    public required StatWeights Health { get; init; }
    public required StatWeights Mana { get; init; }
    public required StatWeights HealthRecovery { get; init; }
    public required StatWeights ManaRecovery { get; init; }
    public required StatWeights HealthItem { get; init; }
    public required StatWeights ManaItem { get; init; }
    public required StatWeights AttackSpeed { get; init; }
    public required StatWeights CastSpeed { get; init; }
    public required StatWeights CriticalOdds { get; init; }
    public required double BaseCriticalChance { get; init; }
    public required double DefenseScale { get; init; }
    public double BaseBlockDamage { get; init; } = .3;

    public void Validate()
    {
        StatWeights?[] rules = [MeleeAttack, RangedAttack, MagicAttack, MeleeWeapon,
            RangedWeapon, PhysicalDefense, MagicDefense, Health, Mana, HealthRecovery,
            ManaRecovery, HealthItem, ManaItem, AttackSpeed, CastSpeed, CriticalOdds];
        if (rules.Any(rule => rule is null || !rule.IsValid()) ||
            !double.IsFinite(BaseCriticalChance) || BaseCriticalChance <= 0 || BaseCriticalChance >= 1 ||
            !double.IsFinite(DefenseScale) || DefenseScale <= 0 || !double.IsFinite(BaseBlockDamage))
            throw new ArgumentException("Invalid balance: coefficients must be finite/non-negative, " +
                "critical chance in (0, 1), defense scale positive.");
    }
}
