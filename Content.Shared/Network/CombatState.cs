using System.Numerics;

namespace Content.Shared.Network;

/// <summary>Only revealed presentation state, not definitions, stats, formulas or RNG.</summary>
public readonly record struct CombatState(NetworkEntityId EntityId, uint ServerTick,
    CombatEntityKind Kind, Vector2 Position, double Health, double MaxHealth,
    double AttackInterval, float Range, float HalfAngleRadians, float Height = 0);
