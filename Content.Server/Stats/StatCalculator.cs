using Content.Server.Data;

namespace Content.Server.Stats;

/// <summary>Pure authoritative calculations: no RNG, transport, Godot or per-tick allocations.</summary>
public sealed class StatCalculator
{
    private readonly StatBalance _balance;

    public StatCalculator(StatBalance balance)
    {
        balance.Validate();
        _balance = balance;
    }

    public DerivedStats Calculate(CreatureDefinition creature) => Calculate(creature.Stats,
        creature.BaseHealth, creature.BaseMana, creature.BaseHealthRecovery, creature.BaseManaRecovery,
        creature.Modifiers);

    public DerivedStats Calculate(BaseStats stats, double baseHealth, double baseMana,
        double baseHealthRecovery, double baseManaRecovery, DerivedStatModifiers? modifiers = null)
    {
        RequirePositive(baseHealth);
        RequirePositive(baseMana);
        RequireNonNegative(baseHealthRecovery);
        RequireNonNegative(baseManaRecovery);
        var odds = StatMath.ScalePositive(_balance.BaseCriticalChance / (1 - _balance.BaseCriticalChance),
            _balance.CriticalOdds.Evaluate(stats));
        // Stable odds-to-probability conversion, including very large odds.
        var critical = odds <= 1 ? odds / (1 + odds) : 1 - 1 / (1 + odds);
        var result = new DerivedStats(
            _balance.MeleeAttack.Evaluate(stats), _balance.RangedAttack.Evaluate(stats),
            _balance.MagicAttack.Evaluate(stats),
            StatMath.PositiveScale(_balance.MeleeWeapon.Evaluate(stats)),
            StatMath.PositiveScale(_balance.RangedWeapon.Evaluate(stats)),
            _balance.PhysicalDefense.Evaluate(stats), _balance.MagicDefense.Evaluate(stats),
            StatMath.ScalePositive(baseHealth, _balance.Health.Evaluate(stats)),
            StatMath.ScalePositive(baseMana, _balance.Mana.Evaluate(stats)),
            Recovery(baseHealthRecovery, _balance.HealthRecovery.Evaluate(stats)),
            Recovery(baseManaRecovery, _balance.ManaRecovery.Evaluate(stats)),
            StatMath.PositiveScale(_balance.HealthItem.Evaluate(stats)),
            StatMath.PositiveScale(_balance.ManaItem.Evaluate(stats)),
            StatMath.PositiveScale(_balance.AttackSpeed.Evaluate(stats)),
            StatMath.PositiveScale(_balance.CastSpeed.Evaluate(stats)), critical, _balance.BaseBlockDamage);
        return modifiers?.Apply(result) ?? result;
    }

    public double WeaponPower(WeaponDefinition weapon, DerivedStats stats)
    {
        RequireNonNegative(weapon.Attack);
        var (status, multiplier) = weapon.Kind switch
        {
            WeaponKind.Melee => (stats.MeleeAttack, stats.MeleeWeaponMultiplier),
            WeaponKind.Ranged => (stats.RangedAttack, stats.RangedWeaponMultiplier),
            WeaponKind.Magic => (stats.MagicAttack, 1d),
            _ => throw new ArgumentException("Unknown weapon kind.")
        };
        // Negative status attack weakens a weapon, but cannot turn damage into healing.
        return Math.Max(0, StatMath.Add(status, StatMath.Multiply(weapon.Attack, multiplier)));
    }

    public double AttackInterval(double baseInterval, DerivedStats stats) =>
        Duration(baseInterval, stats.AttackSpeedMultiplier);

    public double CastDuration(double baseDuration, DerivedStats stats) =>
        Duration(baseDuration, stats.CastSpeedMultiplier);

    /// <summary>Raw crit stat can be signed; a probability is necessarily in [0, 1].</summary>
    public static double CriticalProbability(DerivedStats stats) => Math.Clamp(stats.CriticalChance, 0, 1);

    public double ApplyDefense(double damage, double defense)
    {
        RequireNonNegative(damage);
        if (!double.IsFinite(defense))
            throw new ArgumentException("Defense must be finite.");
        var ratio = defense / _balance.DefenseScale;
        var multiplier = defense >= 0
            ? 1 / StatMath.Add(1, ratio)
            : StatMath.Add(1, -ratio);
        // Tiny defense scales may overflow the division; keep the multiplier finite.
        multiplier = Math.Min(double.MaxValue, multiplier);
        return StatMath.Multiply(damage, multiplier);
    }

    private static double Duration(double duration, double speed)
    {
        RequireNonNegative(duration);
        if (!double.IsFinite(speed))
            throw new ArgumentException("Speed stat must be finite.");
        // Map the signed raw speed stat to a positive execution rate, continuous through zero.
        var effectiveSpeed = StatMath.PositiveScale(StatMath.Add(speed, -1));
        return duration == 0 ? 0 : StatMath.DividePositive(duration, effectiveSpeed);
    }

    private static double Recovery(double baseline, double bonus) =>
        bonus >= 0 ? StatMath.Add(baseline, bonus) : StatMath.Multiply(baseline, StatMath.PositiveScale(bonus));

    private static void RequirePositive(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentException("Expected a finite positive value.");
    }

    private static void RequireNonNegative(double value)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentException("Expected a finite non-negative value.");
    }
}
