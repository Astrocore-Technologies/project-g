using System.Numerics;

namespace Content.Shared.Network;

/// <summary>Intent only: ownership, origin, target, weapon and damage are server decisions.</summary>
public readonly record struct AttackCommand(uint Sequence, uint ClientTick, Vector2 Direction);
