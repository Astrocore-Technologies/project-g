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
    public DerivedStats Stats { get; }
    public WeaponDefinition Weapon { get; }
    public double AttackInterval { get; }
    internal uint LastSequence { get; set; }
    internal uint? LastRequestTick { get; set; }
    internal double ReadyAt { get; set; }
}
