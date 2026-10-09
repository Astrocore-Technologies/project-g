using System.Numerics;

namespace Content.Shared.Network;

/// <summary>Aim is a world point for GroundArea, unit direction for Projectile/Dash. Never a hit claim.</summary>
public readonly record struct AbilityCommand(uint Sequence, uint ObservedServerTick, ushort AbilityId, Vector2 Aim, float DashDistance = 0);
