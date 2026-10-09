using System.Numerics;

namespace Content.Shared.Network;

/// <summary>Intent only: optional target is a request; server validates target, range, ownership and damage.</summary>
public readonly record struct AttackCommand(uint Sequence, uint ClientTick, Vector2 Direction, NetworkEntityId TargetId = default);
