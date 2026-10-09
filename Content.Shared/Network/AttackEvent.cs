using System.Numerics;

namespace Content.Shared.Network;

/// <summary>Reliable authoritative action, including accepted swings that miss.</summary>
public readonly record struct AttackEvent(
    NetworkEntityId AttackerId, uint Sequence, uint ServerTick,
    Vector2 Origin, Vector2 Direction, float Range,
    NetworkEntityId TargetId, double Damage, double TargetHealth, bool Critical, GuardImpact Guard = GuardImpact.None);
