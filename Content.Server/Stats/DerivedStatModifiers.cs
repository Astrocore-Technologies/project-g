namespace Content.Server.Stats;

/// <summary>Signed derived modifiers for future equipment/effects, not changes to primary stats.</summary>
public sealed record DerivedStatModifiers
{
    public double MeleeAttack { get; init; }
    public double RangedAttack { get; init; }
    public double MagicAttack { get; init; }
    public double MeleeWeaponMultiplier { get; init; }
    public double RangedWeaponMultiplier { get; init; }
    public double PhysicalDefense { get; init; }
    public double MagicDefense { get; init; }
    public double MaxHealth { get; init; }
    public double MaxMana { get; init; }
    public double HealthRecovery { get; init; }
    public double ManaRecovery { get; init; }
    public double HealthItemMultiplier { get; init; }
    public double ManaItemMultiplier { get; init; }
    public double AttackSpeedMultiplier { get; init; }
    public double CastSpeedMultiplier { get; init; }
    public double CriticalChance { get; init; }

    public bool IsValid() => double.IsFinite(MeleeAttack) && double.IsFinite(RangedAttack) &&
        double.IsFinite(MagicAttack) && double.IsFinite(MeleeWeaponMultiplier) &&
        double.IsFinite(RangedWeaponMultiplier) && double.IsFinite(PhysicalDefense) &&
        double.IsFinite(MagicDefense) && double.IsFinite(MaxHealth) && double.IsFinite(MaxMana) &&
        double.IsFinite(HealthRecovery) && double.IsFinite(ManaRecovery) &&
        double.IsFinite(HealthItemMultiplier) && double.IsFinite(ManaItemMultiplier) &&
        double.IsFinite(AttackSpeedMultiplier) && double.IsFinite(CastSpeedMultiplier) &&
        double.IsFinite(CriticalChance);

    public DerivedStats Apply(DerivedStats value)
    {
        if (!IsValid())
            throw new ArgumentException("Derived modifiers must be finite.");
        return new DerivedStats(
            StatMath.Add(value.MeleeAttack, MeleeAttack), StatMath.Add(value.RangedAttack, RangedAttack),
            StatMath.Add(value.MagicAttack, MagicAttack),
            StatMath.Add(value.MeleeWeaponMultiplier, MeleeWeaponMultiplier),
            StatMath.Add(value.RangedWeaponMultiplier, RangedWeaponMultiplier),
            StatMath.Add(value.PhysicalDefense, PhysicalDefense), StatMath.Add(value.MagicDefense, MagicDefense),
            Math.Max(double.Epsilon, StatMath.Add(value.MaxHealth, MaxHealth)),
            StatMath.Add(value.MaxMana, MaxMana),
            StatMath.Add(value.HealthRecovery, HealthRecovery), StatMath.Add(value.ManaRecovery, ManaRecovery),
            StatMath.Add(value.HealthItemMultiplier, HealthItemMultiplier),
            StatMath.Add(value.ManaItemMultiplier, ManaItemMultiplier),
            StatMath.Add(value.AttackSpeedMultiplier, AttackSpeedMultiplier),
            StatMath.Add(value.CastSpeedMultiplier, CastSpeedMultiplier),
            StatMath.Add(value.CriticalChance, CriticalChance));
    }
}
