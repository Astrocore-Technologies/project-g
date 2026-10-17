using System.Numerics;
using Content.Shared.Combat;

namespace Content.Shared.Network;

/// <summary>Revealed execution/presentation only. A phase update is never a hit or damage claim.</summary>
public readonly record struct BasicActionState(NetworkEntityId ActorId,ulong ExecutionId,uint Sequence,uint ServerTick,
    CombatActionPhase Phase,Vector2 Position,Vector2 Direction,float Height,float Range,float HalfAngle,
    double PreparationSeconds,double ActiveSeconds,double RecoverySeconds,double RemainingSeconds,
    float MovementMultiplier,bool CanTurn,ActionCancelPermission AllowedCancels,
    float DirectionY=0,float OriginHeightOffset=0,float Thickness=0,BasicAttackDelivery Delivery=BasicAttackDelivery.MeleeArc);
