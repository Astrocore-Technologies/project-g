using Content.Server.Stats;

namespace Content.Server.Combat;

/// <summary>One mutable resource balance per combat actor, independent of its skill loadout.</summary>
internal sealed class CombatResources(DerivedStats stats)
{
    internal double Mana = Math.Max(0, stats.MaxMana);
    internal double Stamina = Math.Max(0, stats.MaxStamina);
    internal bool ManaRecoveryConnected = true;
    // Positive flow that would overflow a full bar can fund work in this same timeline segment.
    internal double ManaFlowCredit;

}
