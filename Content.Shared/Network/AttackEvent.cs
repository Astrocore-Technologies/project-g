using System.Numerics;
using Content.Shared.Combat;

namespace Content.Shared.Network;

/// <summary>Reliable authoritative action, including accepted swings that miss.</summary>
public readonly record struct AttackEvent(
    NetworkEntityId AttackerId, uint Sequence, uint ServerTick,
    Vector2 Origin, Vector2 Direction, float Range,
    NetworkEntityId TargetId, double Damage, double TargetHealth, bool Critical, GuardImpact Guard = GuardImpact.None,
    ulong ExecutionId = 0, bool Evaded = false,
    float DirectionY=0,float OriginHeight=0,float HalfAngleRadians=0,float Thickness=0,CombatHitTags Tags=CombatHitTags.None,
    BasicAttackDelivery Delivery=BasicAttackDelivery.MeleeArc);
