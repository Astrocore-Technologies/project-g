using System.Numerics;
using Content.Server.Configuration;
using Content.Shared.Movement;
using Content.Shared.Network;
using Content.Shared.Navigation;
using Microsoft.Extensions.Options;

namespace Content.Server.World;

/// <summary>
/// Authoritative movement state for the current test region.
/// It is accessed only by the server polling thread.
/// </summary>
public sealed class ServerWorld
{
    private readonly Dictionary<int, ServerPlayer> _playersByConnection = new();
    private readonly Dictionary<NetworkEntityId, ServerPlayer> _playersByEntity = new();
    private readonly HashSet<int> _movingPlayers = new();
    private readonly List<int> _stoppedPlayers = new();
    private readonly MovementSettings _movement;
    private readonly InterestOptions _interest;
    private readonly SpatialIndex _spatial;
    private readonly NavigationPathfinder _pathfinder;
    private ulong _nextEntityId = 1;

    public ServerWorld(IOptions<MovementOptions> options, IOptions<InterestOptions> interest,
        IOptions<NavigationOptions>? navigation = null)
    {
        _movement = options.Value.ToSettings();
        _interest = interest.Value;
        if (!_interest.IsValid())
            throw new ArgumentException("Invalid interest settings.", nameof(interest));
        _spatial = new SpatialIndex(_interest.CellSize);
        Navigation = (navigation?.Value ?? new NavigationOptions()).CreateGrid(_movement);
        _pathfinder = new NavigationPathfinder(Navigation);
    }

    public uint Tick { get; private set; }
    public NavigationGrid Navigation { get; }
    public IReadOnlyCollection<ServerPlayer> Players => _playersByConnection.Values;

    public ServerPlayer AddPlayer(int connectionId, PlayerId playerId)
    {
        if (!playerId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(playerId));
        if (_playersByConnection.ContainsKey(connectionId))
            throw new ArgumentException("Connection already owns an entity.", nameof(connectionId));
        if (_nextEntityId == 0)
            throw new InvalidOperationException("Runtime entity IDs exhausted.");
        var entityId = new NetworkEntityId(_nextEntityId++);
        var spawnIndex = (int) ((entityId.Value - 1) % 10);
        var spawn = MovementSimulation.ClampTarget(new Vector2(-9f + spawnIndex * 2f, 0f), _movement);
        if (!Navigation.TryFindSpawn(spawn, out spawn))
            throw new InvalidOperationException("Region has no walkable spawn.");
        var player = new ServerPlayer(connectionId, playerId, entityId,
            new NavigationMover(Navigation, _movement, _pathfinder, spawn));
        _playersByConnection.Add(connectionId, player);
        _playersByEntity.Add(entityId, player);
        _spatial.Add(entityId, spawn);
        return player;
    }

    public ServerPlayer? RemovePlayer(int connectionId)
    {
        if (!_playersByConnection.Remove(connectionId, out var player))
            return null;
        _playersByEntity.Remove(player.EntityId);
        _spatial.Remove(player.EntityId);
        _movingPlayers.Remove(connectionId);
        return player;
    }

    public bool TryApplyMove(int connectionId, MoveCommand command)
    {
        if (!_playersByConnection.TryGetValue(connectionId, out var player) ||
            !MovementSimulation.IsSequenceNewer(command.Sequence, player.LastProcessedSequence) ||
            !MovementSimulation.IsValidTarget(command.Target, _movement))
        {
            return false;
        }

        if (command.Target != player.Target)
        {
            // Repeated intentions are cheap; a flood cannot trigger unbounded A* searches.
            if (player.LastPathRequestTick == Tick)
                return false;
            player.LastPathRequestTick = Tick;
            if (!player.Motion.TrySetTarget(command.Target))
                return false;
        }
        player.LastProcessedSequence = command.Sequence;
        if (player.Target != player.Position)
            _movingPlayers.Add(connectionId);
        else
            _movingPlayers.Remove(connectionId);
        return true;
    }

    public void Simulate(float fixedDeltaSeconds)
    {
        Tick++;

        _stoppedPlayers.Clear();
        // Idle players do not require movement work or spatial updates each tick.
        foreach (var connectionId in _movingPlayers)
        {
            var player = _playersByConnection[connectionId];
            player.Motion.Step(fixedDeltaSeconds);
            _spatial.Move(player.EntityId, player.Position);
            if (!player.Motion.IsMoving)
                _stoppedPlayers.Add(connectionId);
        }
        foreach (var connectionId in _stoppedPlayers)
            _movingPlayers.Remove(connectionId);
    }

    public PlayerSpawn CreateSpawn(ServerPlayer player) =>
        new(player.PlayerId, player.EntityId, player.Position, _movement, Tick);

    public PlayerSpawn CreateSpawn(NetworkEntityId id) => CreateSpawn(_playersByEntity[id]);

    public void UpdateInterest(int connectionId, InterestView view)
    {
        view.EnteredIds.Clear();
        view.LeftIds.Clear();
        view.States.Clear();
        var observer = _playersByConnection[connectionId];
        _spatial.Query(observer.Position, _interest.ExitRadius, view.Candidates);

        foreach (var id in view.Visible)
        {
            if (!view.Candidates.Contains(id))
                view.LeftIds.Add(id);
        }
        foreach (var id in view.LeftIds)
            view.Visible.Remove(id);

        var squaredEnterRadius = _interest.Radius * _interest.Radius;
        foreach (var id in view.Candidates)
        {
            var player = _playersByEntity[id];
            if (!view.Visible.Contains(id) &&
                Vector2.DistanceSquared(observer.Position, player.Position) > squaredEnterRadius)
                continue;
            if (view.Visible.Add(id))
                view.EnteredIds.Add(id);
            view.States.Add(new EntitySnapshot(
                player.EntityId,
                player.Position,
                player.LastProcessedSequence,
                player.Target));
        }
    }
}
