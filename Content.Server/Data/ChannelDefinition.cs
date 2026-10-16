using Content.Server.Stats;

namespace Content.Server.Data;

public enum ChannelCooldownStart : byte { Activation, Completion }

/// <summary>Every timing/cost choice is explicit; null maximum means sustain until stopped.</summary>
public sealed record ChannelDefinition
{
    public required double? MaximumSeconds {get;init;}
    public required double IntervalSeconds {get;init;}
    public required ActionSpeedSource IntervalSpeed {get;init;}
    public required bool PulseAtStart {get;init;}
    public required double ManaPerSecond {get;init;}
    public required double StaminaPerSecond {get;init;}
    public required ChannelCooldownStart CooldownStart {get;init;}
    public required EffectCalculationMode DamageCalculation {get;init;}

    internal void Validate(string path)
    {
        if(MaximumSeconds is {} maximum && (!double.IsFinite(maximum) || maximum<=0 || !float.IsFinite((float)maximum) || (float)maximum<=0) ||
            !double.IsFinite(IntervalSeconds) || IntervalSeconds<=0 || !Enum.IsDefined(IntervalSpeed) ||
            !double.IsFinite(ManaPerSecond) || ManaPerSecond<0 || !double.IsFinite(StaminaPerSecond) || StaminaPerSecond<0 ||
            !Enum.IsDefined(CooldownStart) || !Enum.IsDefined(DamageCalculation))
            throw new ArgumentException($"{path}: invalid channel duration, cadence, resource rate or execution policy.");
    }

    internal ChannelDefinition ForExecution(StatCalculator calculator,DerivedStats stats)=>this with
    {
        IntervalSeconds=IntervalSpeed switch
        {
            ActionSpeedSource.AttackSpeed=>calculator.AttackInterval(IntervalSeconds,stats),
            ActionSpeedSource.CastSpeed=>calculator.CastDuration(IntervalSeconds,stats),
            _=>IntervalSeconds
        }
    };
}
