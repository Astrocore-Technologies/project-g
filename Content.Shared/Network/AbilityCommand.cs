using System.Numerics;

namespace Content.Shared.Network;

/// <summary>Aim is a world point for GroundArea, unit direction for Projectile/Dash. Never a hit claim.</summary>
public enum AbilityIntent : byte { Use, Deactivate }
public readonly record struct AbilityCommand(uint Sequence, uint ObservedServerTick, ushort AbilityId, Vector2 Aim, float DashDistance = 0, float AimHeight = 0, float DirectionY = 0,AbilityIntent Intent=AbilityIntent.Use, uint InputOrder = 0)
{
    public uint InputOrder { get; init; } = InputOrder == 0 ? Sequence : InputOrder;
}
