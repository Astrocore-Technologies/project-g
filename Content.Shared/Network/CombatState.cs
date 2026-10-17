using System.Numerics;
using Content.Shared.Combat;

namespace Content.Shared.Network;

/// <summary>Only revealed presentation state, not definitions, stats, formulas or RNG.</summary>
public readonly record struct CombatState(NetworkEntityId EntityId, uint ServerTick,
    CombatEntityKind Kind, Vector2 Position, double Health, double MaxHealth,
    double AttackInterval, float Range, float HalfAngleRadians, float Height = 0,
    CombatBodyProfile Body=default,float BasicOriginHeight=0,float BasicThickness=0,
    BasicAttackDelivery BasicDelivery=BasicAttackDelivery.MeleeArc,float BasicProjectileSpeed=0,float BasicProjectileRadius=0);
