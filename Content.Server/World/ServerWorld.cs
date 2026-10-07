using System.Numerics;
using Content.Server.Configuration;
using Content.Shared.Movement;
using Content.Shared.Network;
using Microsoft.Extensions.Options;

namespace Content.Server.World;

/// <summary>
/// Authoritative movement state for the current test region.
/// It is accessed only by the server polling thread.
/// </summary>
public sealed class ServerWorld
{
    private readonly Dictionary<int, ServerPlayer> _playersByConnection = new();
    private readonly MovementSettings _movement;
    private ulong _nextEntityId = 1;

    public ServerWorld(IOptions<MovementOptions> options)
    {
        _movement = options.Value.ToSettings();
    }

    public uint Tick { get; private set; }
    public IReadOnlyCollection<ServerPlayer> Players => _playersByConnection.Values;

    public ServerPlayer AddPlayer(int connectionId, PlayerId playerId)
    {
        var entityId = new NetworkEntityId(_nextEntityId++);
        var spawnIndex = (int) ((entityId.Value - 1) % 10);
        var spawn = new Vector2(-9f + spawnIndex * 2f, 0f);
        var player = new ServerPlayer(connectionId, playerId, entityId, spawn);
        _playersByConnection.Add(connectionId, player);
        return player;
    }

    public ServerPlayer? RemovePlayer(int connectionId)
    {
        return _playersByConnection.Remove(connectionId, out var player)
            ? player
            : null;
    }

    public bool TryApplyMove(int connectionId, MoveCommand command)
    {
        if (!_playersByConnection.TryGetValue(connectionId, out var player) ||
            !MovementSimulation.IsSequenceNewer(command.Sequence, player.LastProcessedSequence) ||
            !MovementSimulation.IsValidTarget(command.Target, _movement))
        {
            return false;
        }

        player.Target = command.Target;
        player.LastProcessedSequence = command.Sequence;
        return true;
    }

    public void Simulate(float fixedDeltaSeconds)
    {
        Tick++;

        foreach (var player in _playersByConnection.Values)
        {
            player.Position = MovementSimulation.Step(
                player.Position,
                player.Target,
                _movement,
                fixedDeltaSeconds);
        }
    }

    public PlayerSpawn CreateSpawn(ServerPlayer player) =>
        new(player.PlayerId, player.EntityId, player.Position, _movement);

    public WorldSnapshot CreateSnapshot()
    {
        var entities = _playersByConnection.Values
            .Take(NetworkConstants.MaxSnapshotEntities)
            .Select(player => new EntitySnapshot(
                player.EntityId,
                player.Position,
                player.LastProcessedSequence))
            .ToArray();

        return new WorldSnapshot(Tick, entities);
    }
}

public sealed class ServerPlayer(
    int connectionId,
    PlayerId playerId,
    NetworkEntityId entityId,
    Vector2 spawnPosition)
{
    public int ConnectionId { get; } = connectionId;
    public PlayerId PlayerId { get; } = playerId;
    public NetworkEntityId EntityId { get; } = entityId;
    public Vector2 Position { get; set; } = spawnPosition;
    public Vector2 Target { get; set; } = spawnPosition;
    public uint LastProcessedSequence { get; set; }
}
