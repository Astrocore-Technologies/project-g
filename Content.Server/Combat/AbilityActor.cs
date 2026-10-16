using Content.Server.Data;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Owner-only resources, unlocked slots and per-slot cooldowns.</summary>
internal sealed class AbilityActor(AbilityDefinition[] definitions, AbilityProfile[] profiles, ActionDurations[] durations,SkillCooldown[] cooldowns,double maximumMana)
{
    public AbilityDefinition[] Definitions { get; } = definitions;
    public AbilityProfile[] Profiles { get; } = profiles;
    internal ActionDurations[] Durations { get; } = durations;
    public HashSet<ushort> Enabled { get; set; } = profiles.Where(p=>p.ExecutionForm!=Content.Shared.Combat.CombatActionForm.Passive).Select(p => p.Id).ToHashSet();
    public Dictionary<ushort,int> Levels { get; set; } = new();
    internal SkillCooldown[] Cooldowns { get; } = cooldowns;
    public double Mana { get; set; } = maximumMana;
    public double MaxMana { get; } = maximumMana;
    internal double RecoveryElapsed { get; set; } // online simulation time; not persisted.
    public uint LastSeenSequence { get; set; }
    public uint? LastRequestTick { get; set; }
    internal uint LastCancelSequence { get; set; }
    internal uint? LastCancelTick { get; set; }
    internal uint LastControlSequence {get;set;}
    internal uint? LastControlTick {get;set;}
}
