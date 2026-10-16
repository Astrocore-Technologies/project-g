using Content.Server.Stats;

namespace Content.Server.Data;

public enum EffectDamageFamily : byte { Physical, Magical }
public enum PeriodicEffectKind : byte { Damage, Healing }
public enum EffectCalculationMode : byte { Snapshot, Dynamic }
public enum MissingEffectSource : byte { Unspecified, UseLastSnapshot, Expire }
public enum ShieldRefreshMode : byte { Replace, Add }

public sealed record EffectPower
{
    public double Flat { get; init; }
    public double MeleeScale { get; init; }
    public double RangedScale { get; init; }
    public double MagicScale { get; init; }
    public double WeaponScale { get; init; }

    internal double Evaluate(DerivedStats source,double weaponPower=0) => Math.Max(0,StatMath.Add(StatMath.Add(Flat,StatMath.Multiply(weaponPower,WeaponScale)),
        StatMath.Add(StatMath.Multiply(source.MeleeAttack,MeleeScale),StatMath.Add(
            StatMath.Multiply(source.RangedAttack,RangedScale),StatMath.Multiply(source.MagicAttack,MagicScale)))));
    internal void Validate()
    {
        if(!double.IsFinite(Flat) || Flat<0 || !double.IsFinite(MeleeScale) || MeleeScale<0 ||
            !double.IsFinite(RangedScale) || RangedScale<0 || !double.IsFinite(MagicScale) || MagicScale<0 || !double.IsFinite(WeaponScale) || WeaponScale<0)
            throw new ArgumentException("Invalid effect power.");
    }
}

public sealed record PeriodicEffectDefinition
{
    public required PeriodicEffectKind Kind { get; init; }
    public required double IntervalSeconds { get; init; }
    public required EffectPower Power { get; init; }
    public EffectDamageFamily DamageFamily { get; init; }
    public EffectCalculationMode Calculation { get; init; }
    public MissingEffectSource MissingSource { get; init; }

    internal void Validate()
    {
        if(!Enum.IsDefined(Kind) || !Enum.IsDefined(DamageFamily) || !Enum.IsDefined(Calculation) || !Enum.IsDefined(MissingSource) ||
            !double.IsFinite(IntervalSeconds) || IntervalSeconds<=0 || Calculation==EffectCalculationMode.Dynamic && MissingSource==MissingEffectSource.Unspecified)
            throw new ArgumentException("Invalid periodic effect or missing dynamic source policy.");
        ArgumentNullException.ThrowIfNull(Power); Power.Validate();
    }
}

public sealed record ShieldEffectDefinition
{
    public required EffectPower Power { get; init; }
    public ShieldRefreshMode Refresh { get; init; }
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Power); Power.Validate();
        if(!Enum.IsDefined(Refresh)) throw new ArgumentException("Invalid shield refresh policy.");
    }
}

public sealed record DamageProcDefinition
{
    public required double Fraction { get; init; }
    public bool Direct { get; init; }
    public bool Periodic { get; init; }
    public bool Physical { get; init; } = true;
    public bool Magical { get; init; } = true;
    internal void Validate()
    {
        if(!double.IsFinite(Fraction) || Fraction<0 || !Direct && !Periodic || !Physical && !Magical) throw new ArgumentException("Invalid damage proc.");
    }
}
