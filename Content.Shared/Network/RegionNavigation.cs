using System.Numerics;

namespace Content.Shared.Network;

/// <summary>Public geometry of the current prototype region; no secret world state.</summary>
public readonly record struct RegionNavigation(
    Vector2 Origin, float CellSize, float AgentRadius,
    ushort Width, ushort Height, byte[] BlockedCells);
