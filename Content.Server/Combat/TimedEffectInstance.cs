using Content.Server.Data;

namespace Content.Server.Combat;

internal readonly record struct TimedEffectKey(CombatSourceId Source, string DefinitionId);

internal sealed class TimedEffectInstance(Guid id, TimedEffectKey key, EffectDefinition definition,
    double endsAt, ushort stacks, ulong appliedOrder)
{
    internal Guid Id { get; } = id;
    internal TimedEffectKey Key { get; } = key;
    internal EffectDefinition Definition { get; } = definition;
    internal double EndsAt { get; set; } = endsAt;
    internal ushort Stacks { get; set; } = stacks;
    internal ulong AppliedOrder { get; set; } = appliedOrder;
    internal EffectSnapshot Snapshot { get; set; }
    internal double? NextTickAt { get; set; }
    internal double ShieldRemaining { get; set; }
    internal double PowerScale { get; set; } = 1;
    internal ulong ExecutionId { get; set; }
}

public readonly record struct EffectSnapshot(double Power,double CriticalChance,double CriticalDamage)
{
    internal bool IsValid=>double.IsFinite(Power) && Power>=0 && double.IsFinite(CriticalChance) && CriticalChance is >=0 and <=1 &&
        double.IsFinite(CriticalDamage) && CriticalDamage>=0;
}
