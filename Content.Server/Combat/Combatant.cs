using System.Numerics;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Runtime data only; mutations belong to CombatSimulation.</summary>
public sealed class Combatant
{
    internal Combatant(NetworkEntityId id, CombatEntityKind kind, Vector2 position,
        DerivedStats stats, WeaponDefinition weapon, double interval)
    {
        Id = id; Kind = kind; Position = position; Stats = stats; Weapon = weapon;
        Health = stats.MaxHealth; AttackInterval = interval;
    }

    public NetworkEntityId Id { get; }
    public CombatEntityKind Kind { get; }
    public Vector2 Position { get; internal set; }
    public double Health { get; internal set; }
    internal double HealthRecoveryElapsed { get; set; }
    public DerivedStats Stats { get; internal set; }
    public WeaponDefinition Weapon { get; internal set; }
    public double AttackInterval { get; internal set; }
    public bool IsCasting { get; internal set; }
    internal bool CanBleed { get; set; }
    internal bool CanBeStunned { get; set; }
    internal bool StationaryCast { get; set; }
    internal double StunnedUntil { get; set; }
    internal double SlowUntil { get; set; }
    internal float SlowFraction { get; set; }
    public uint LastAbilitySequence { get; internal set; }
    internal uint LastSequence { get; set; }
    internal uint? LastRequestTick { get; set; }
    internal double ReadyAt { get; set; }
}
