using Content.Server.Data;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Owner-only resources, unlocked slots and per-slot cooldowns.</summary>
internal sealed class AbilityActor(AbilityDefinition[] definitions, AbilityProfile[] profiles, double maximumMana)
{
    public AbilityDefinition[] Definitions { get; } = definitions;
    public AbilityProfile[] Profiles { get; } = profiles;
    public HashSet<ushort> Enabled { get; set; } = profiles.Select(p => p.Id).ToHashSet();
    public Dictionary<ushort,int> Levels { get; set; } = new();
    public double[] ReadyAt { get; } = new double[definitions.Length];
    public double Mana { get; set; } = maximumMana;
    public double MaxMana { get; } = maximumMana;
    internal double RecoveryElapsed { get; set; } // online simulation time; not persisted.
    public uint LastSeenSequence { get; set; }
    public uint? LastRequestTick { get; set; }
}
