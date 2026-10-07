using System.Numerics;
using Content.Shared.Network;

namespace Content.Server.World;

/// <summary>Runtime player data; mutations belong to ServerWorld so the spatial index stays valid.</summary>
public sealed class ServerPlayer(
    int connectionId,
    PlayerId playerId,
    NetworkEntityId entityId,
    Vector2 spawnPosition)
{
    public int ConnectionId { get; } = connectionId;
    public PlayerId PlayerId { get; } = playerId;
    public NetworkEntityId EntityId { get; } = entityId;
    public Vector2 Position { get; internal set; } = spawnPosition;
    public Vector2 Target { get; internal set; } = spawnPosition;
    public uint LastProcessedSequence { get; internal set; }
}
