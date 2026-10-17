namespace Content.Server.Stats;

/// <summary>Signed derived modifiers for future equipment/effects, not changes to primary stats.</summary>
public sealed record DerivedStatModifiers
{
    private static readonly DerivedStatModifiers Empty=new();
    internal bool IsEmpty=>this==Empty;
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
    public double MaxStamina { get; init; }
    public double StaminaRecovery { get; init; }
    public double HealthItemMultiplier { get; init; }
    public double ManaItemMultiplier { get; init; }
    public double AttackSpeedMultiplier { get; init; }
    public double CastSpeedMultiplier { get; init; }
    public double CriticalChance { get; init; }
    public double BlockDamage { get; init; }
    public double Accuracy { get; init; }
    public double Evasion { get; init; }
    public double CriticalDamage { get; init; }
    public double BlockStrength { get; init; }
    public double CooldownReduction { get; init; }

    public bool IsValid() => double.IsFinite(MeleeAttack) && double.IsFinite(RangedAttack) &&
        double.IsFinite(MagicAttack) && double.IsFinite(MeleeWeaponMultiplier) &&
        double.IsFinite(RangedWeaponMultiplier) && double.IsFinite(PhysicalDefense) &&
        double.IsFinite(MagicDefense) && double.IsFinite(MaxHealth) && double.IsFinite(MaxMana) &&
        double.IsFinite(HealthRecovery) && double.IsFinite(ManaRecovery) && double.IsFinite(MaxStamina) && double.IsFinite(StaminaRecovery) &&
        double.IsFinite(HealthItemMultiplier) && double.IsFinite(ManaItemMultiplier) &&
        double.IsFinite(AttackSpeedMultiplier) && double.IsFinite(CastSpeedMultiplier) &&
        double.IsFinite(CriticalChance) && double.IsFinite(BlockDamage) && double.IsFinite(Accuracy) && double.IsFinite(Evasion) && double.IsFinite(CriticalDamage) && double.IsFinite(BlockStrength) && double.IsFinite(CooldownReduction);

    public DerivedStats Apply(DerivedStats value, double stacks = 1, bool clampMaxHealth = true)
    {
        if (!IsValid() || !double.IsFinite(stacks) || stacks <= 0)
            throw new ArgumentException("Derived modifiers must be finite.");
        return new DerivedStats(
            StatMath.Add(value.MeleeAttack, StatMath.Multiply(MeleeAttack, stacks)), StatMath.Add(value.RangedAttack, StatMath.Multiply(RangedAttack, stacks)),
            StatMath.Add(value.MagicAttack, StatMath.Multiply(MagicAttack, stacks)),
            StatMath.Add(value.MeleeWeaponMultiplier, StatMath.Multiply(MeleeWeaponMultiplier, stacks)),
            StatMath.Add(value.RangedWeaponMultiplier, StatMath.Multiply(RangedWeaponMultiplier, stacks)),
            StatMath.Add(value.PhysicalDefense, StatMath.Multiply(PhysicalDefense, stacks)), StatMath.Add(value.MagicDefense, StatMath.Multiply(MagicDefense, stacks)),
            clampMaxHealth ? Math.Max(double.Epsilon, StatMath.Add(value.MaxHealth, StatMath.Multiply(MaxHealth, stacks))) : StatMath.Add(value.MaxHealth, StatMath.Multiply(MaxHealth, stacks)),
            StatMath.Add(value.MaxMana, StatMath.Multiply(MaxMana, stacks)),
            StatMath.Add(value.HealthRecovery, StatMath.Multiply(HealthRecovery, stacks)), StatMath.Add(value.ManaRecovery, StatMath.Multiply(ManaRecovery, stacks)),
            StatMath.Add(value.HealthItemMultiplier, StatMath.Multiply(HealthItemMultiplier, stacks)),
            StatMath.Add(value.ManaItemMultiplier, StatMath.Multiply(ManaItemMultiplier, stacks)),
            StatMath.Add(value.AttackSpeedMultiplier, StatMath.Multiply(AttackSpeedMultiplier, stacks)),
            StatMath.Add(value.CastSpeedMultiplier, StatMath.Multiply(CastSpeedMultiplier, stacks)),
            StatMath.Add(value.CriticalChance, StatMath.Multiply(CriticalChance, stacks)), StatMath.Add(value.BlockDamage, StatMath.Multiply(BlockDamage, stacks)),
            StatMath.Add(value.Accuracy, StatMath.Multiply(Accuracy, stacks)), StatMath.Add(value.Evasion, StatMath.Multiply(Evasion, stacks)), StatMath.Add(value.CriticalDamage, StatMath.Multiply(CriticalDamage, stacks)),
            StatMath.Add(value.BlockStrength, StatMath.Multiply(BlockStrength, stacks)),StatMath.Add(value.CooldownReduction,StatMath.Multiply(CooldownReduction,stacks)),
            StatMath.Add(value.MaxStamina, StatMath.Multiply(MaxStamina, stacks)), StatMath.Add(value.StaminaRecovery, StatMath.Multiply(StaminaRecovery, stacks)));
    }
}
