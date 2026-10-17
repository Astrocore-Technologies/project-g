using Content.Server.Stats;

namespace Content.Server.Data;

public enum EffectDisposition : byte { Beneficial, Harmful, Neutral }

/// <summary>Server-only timed attribute effect. Periodic damage/healing are separate effect operations.</summary>
public sealed record EffectDefinition
{
    public required string Id { get; init; }
    public required double DurationSeconds { get; init; }
    public EffectDisposition Disposition { get; init; }
    public DerivedStatModifiers Modifiers { get; init; } = new();
    public ControlResistance ControlResistance {get;init;}=new();
    public PeriodicEffectDefinition? Periodic { get; init; }
    public ShieldEffectDefinition? Shield { get; init; }
    public DamageProcDefinition? LifeSteal { get; init; }
    public DamageProcDefinition? ManaSteal { get; init; }
    public DamageProcDefinition? Reflection { get; init; }
    public bool RemoveOnSourceDeath { get; init; }
    public bool AllowsUnarmedBlock { get; init; }
    public ushort MaxStacks { get; init; } = 1;
    // Zero means refresh without gaining stacks. A stacking definition explicitly authors the increment.
    public ushort StackIncrement { get; init; }
    public bool PersistThroughDeath { get; init; }
    public bool PersistThroughLogout { get; init; } = true;
    public bool Dispellable { get; init; }
    public bool Cleansable { get; init; }
    public string[] Tags { get; init; } = [];
    public string[] ImmuneTags { get; init; } = [];
    public string[] RequiredEnvironmentTags { get; init; } = [];
    public string[] ExcludedEnvironmentTags { get; init; } = [];

    internal void Validate()
    {
        if (!double.IsFinite(DurationSeconds) || DurationSeconds <= 0 || !Enum.IsDefined(Disposition) ||
            Modifiers is null || !Modifiers.IsValid() || MaxStacks == 0 || StackIncrement > MaxStacks ||
            Dispellable && Disposition != EffectDisposition.Beneficial || Cleansable && Disposition != EffectDisposition.Harmful ||
            AllowsUnarmedBlock && Disposition!=EffectDisposition.Beneficial)
            throw new ArgumentException($"effect {Id}: invalid duration, modifiers, stacks or removal policy");
        ValidateTags(Tags); ValidateTags(ImmuneTags); ValidateTags(RequiredEnvironmentTags); ValidateTags(ExcludedEnvironmentTags);
        ArgumentNullException.ThrowIfNull(ControlResistance);ControlResistance.Validate();
        Periodic?.Validate(); Shield?.Validate(); LifeSteal?.Validate(); ManaSteal?.Validate(); Reflection?.Validate();
        if(Periodic is {Kind:PeriodicEffectKind.Damage} && Disposition!=EffectDisposition.Harmful ||
            Periodic is {Kind:PeriodicEffectKind.Healing} && Disposition!=EffectDisposition.Beneficial || Shield is not null && Disposition!=EffectDisposition.Beneficial)
            throw new ArgumentException($"effect {Id}: inconsistent operation disposition");
        foreach (var tag in RequiredEnvironmentTags)
            if (Array.IndexOf(ExcludedEnvironmentTags, tag) >= 0) throw new ArgumentException($"effect {Id}: contradictory environment condition");
    }

    internal static void ValidateTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (tag is not { Length: > 0 and <= 64 } || !seen.Add(tag)) throw new ArgumentException("Invalid or duplicate effect tag.");
            foreach (var c in tag)
                if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.')) throw new ArgumentException("Invalid effect tag syntax.");
        }
    }
}
