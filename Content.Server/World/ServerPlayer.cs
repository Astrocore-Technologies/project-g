using System.Numerics;
using Content.Shared.Network;
using Content.Shared.Navigation;
using Content.Server.Stats;

namespace Content.Server.World;

/// <summary>Runtime player data; mutations belong to ServerWorld so the spatial index stays valid.</summary>
public sealed class ServerPlayer(
    int connectionId,
    PlayerId playerId,
    NetworkEntityId entityId,
    NavigationMover motion)
{
    public int ConnectionId { get; internal set; } = connectionId;
    public PlayerId PlayerId { get; internal set; } = playerId;
    public NetworkEntityId EntityId { get; } = entityId;
    public Vector2 Position => Motion.Position;
    public Vector2 Target => Motion.Target;
    public float Height => Motion.Height;
    public Vector3 Foot => Motion.Foot;
    public bool Loaded { get; internal set; } = true;
    internal NavigationMover Motion { get; } = motion;
    internal uint? LastPathRequestTick { get; set; }
    public uint LastProcessedSequence { get; internal set; }
    internal BaseStats BaseStats { get; set; }
}
