using Content.Server.Stats;

namespace Content.Server.Combat;

/// <summary>One mutable resource balance per combat actor, independent of its skill loadout.</summary>
internal sealed class CombatResources(DerivedStats stats)
{
    internal double Mana = Math.Max(0, stats.MaxMana);
    internal double Stamina = Math.Max(0, stats.MaxStamina);
    internal double ManaRecoveryElapsed;
    internal bool ManaRecoveryConnected = true;

    internal void Clamp(DerivedStats current)
    {
        Mana = Math.Clamp(Mana, 0, Math.Max(0, current.MaxMana));
        Stamina = Math.Clamp(Stamina, 0, Math.Max(0, current.MaxStamina));
    }
}
