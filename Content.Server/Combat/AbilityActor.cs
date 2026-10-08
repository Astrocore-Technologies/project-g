using Content.Server.Data;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Owner-only resources, unlocked slots and per-slot cooldowns.</summary>
internal sealed class AbilityActor(AbilityDefinition[] definitions, AbilityProfile[] profiles, double maximumMana)
{
    public AbilityDefinition[] Definitions { get; } = definitions;
    public AbilityProfile[] Profiles { get; } = profiles;
    public double[] ReadyAt { get; } = new double[definitions.Length];
    public double Mana { get; set; } = maximumMana;
    public double MaxMana { get; } = maximumMana;
    public uint LastSeenSequence { get; set; }
    public uint? LastRequestTick { get; set; }
}
