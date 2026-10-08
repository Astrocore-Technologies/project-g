using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Combat;
using Content.Server.Data;
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
    private readonly CreatureDefinition? _playerDefinition;
    private ulong _nextEntityId = 1;

    public ServerWorld(IOptions<MovementOptions> options, IOptions<InterestOptions> interest,
        IOptions<NavigationOptions>? navigation = null, ContentCatalog? catalog = null,
        IOptions<CombatOptions>? combat = null, IOptions<ServerOptions>? server = null, IOptions<NpcOptions>? npc = null)
    {
        _movement = options.Value.ToSettings();
        _interest = interest.Value;
        if (!_interest.IsValid())
            throw new ArgumentException("Invalid interest settings.", nameof(interest));
        _spatial = new SpatialIndex(_interest.CellSize);
        Navigation = (navigation?.Value ?? new NavigationOptions()).CreateGrid(_movement);
        _pathfinder = new NavigationPathfinder(Navigation);
        if (catalog is not null)
        {
            var settings = combat?.Value ?? new CombatOptions();
            _playerDefinition = catalog.Creatures[settings.PlayerDefinitionId];
            Combat = new CombatSimulation(catalog, _spatial, Navigation, settings,
                server?.Value.TickRate ?? NetworkConstants.ServerTickRate, _interest.CellSize);
            var position = new Vector2(settings.TargetX, settings.TargetZ);
            if (!Navigation.IsWalkable(position))
                throw new ArgumentException("Training target must have a walkable configured position.");
            TrainingTargetId = new NetworkEntityId(_nextEntityId++);
            Combat.Add(TrainingTargetId, position, CombatEntityKind.TrainingTarget);
            _spatial.Add(TrainingTargetId, position);
            Abilities = new AbilitySimulation(catalog, Combat, _spatial, Navigation, settings, _interest.CellSize, StartDash);
            if (npc?.Value is { Enabled: true } npcSettings)
            {
                if (!catalog.Creatures.ContainsKey(npcSettings.DefinitionId))
                    throw new ArgumentException("NPC creature definition is missing.");
                var id = new NetworkEntityId(_nextEntityId++);
                var home = new Vector2(npcSettings.X, npcSettings.Z);
                Combat.Add(id, home, CombatEntityKind.Monster, npcSettings.DefinitionId);
                _spatial.Add(id, home);
                Npc = new NpcSimulation(id, Combat, _spatial, Navigation, _movement, npcSettings, _interest.CellSize);
            }
        }
    }

    public uint Tick { get; private set; }
    public NavigationGrid Navigation { get; }
    public CombatSimulation? Combat { get; }
    public AbilitySimulation? Abilities { get; }
    public NetworkEntityId TrainingTargetId { get; }
    public NpcSimulation? Npc { get; }
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
        Combat?.Add(entityId, spawn, CombatEntityKind.Player);
        if (Abilities is not null && _playerDefinition is { } definition)
            Abilities.AddPlayer(entityId, definition);
        return player;
    }

    public ServerPlayer? RemovePlayer(int connectionId)
    {
        if (!_playersByConnection.Remove(connectionId, out var player))
            return null;
        _playersByEntity.Remove(player.EntityId);
        _spatial.Remove(player.EntityId);
        _movingPlayers.Remove(connectionId);
        Combat?.Remove(player.EntityId);
        Abilities?.Remove(player.EntityId);
        return player;
    }

    public bool TryApplyMove(int connectionId, MoveCommand command)
    {
        if (!_playersByConnection.TryGetValue(connectionId, out var player) ||
            !MovementSimulation.IsSequenceNewer(command.Sequence, player.LastProcessedSequence) ||
            (Combat is not null && Combat.Get(player.EntityId).Health <= 0) ||
            !MovementSimulation.IsValidTarget(command.Target, _movement))
        {
            return false;
        }

        if (command.Target != player.Target)
        {
            if (player.Motion.IsDashing)
            {
                player.LastProcessedSequence = command.Sequence;
                return true;
            }
            // Repeated intentions are cheap; a flood cannot trigger unbounded A* searches.
            if (player.LastPathRequestTick == Tick)
                return false;
            player.LastPathRequestTick = Tick;
            if (!player.Motion.TrySetTarget(command.Target))
                return false;
        }
        player.LastProcessedSequence = command.Sequence;
        if (player.Motion.IsMoving)
            _movingPlayers.Add(connectionId);
        else
            _movingPlayers.Remove(connectionId);
        return true;
    }

    public bool TryQueueAttack(int connectionId, AttackCommand command) =>
        _playersByConnection.TryGetValue(connectionId, out var player) &&
        Combat?.Queue(player.EntityId, command, Tick) == true;

    public bool TryQueueAbility(int connectionId, AbilityCommand command, int measuredRttMilliseconds) =>
        _playersByConnection.TryGetValue(connectionId, out var player) &&
        Abilities?.Queue(player.EntityId, command, Tick, measuredRttMilliseconds) == true;

    private bool StartDash(NetworkEntityId id, Vector2 destination, float speed)
    {
        if (!_playersByEntity.TryGetValue(id, out var player) || Combat?.Get(id).Health <= 0 ||
            !player.Motion.TryStartDash(destination, speed)) return false;
        _movingPlayers.Add(player.ConnectionId);
        return true;
    }

    public void UpdateAbilityInterest(int connectionId, InterestView entities, AbilityInterestView effects) =>
        Abilities?.UpdateInterest(effects, entities.Entities, _playersByConnection[connectionId].Position,
            _interest.Radius, _interest.ExitRadius, Tick);

    public bool IsPlayer(NetworkEntityId id) => _playersByEntity.ContainsKey(id);

    public bool TryGetOwnedEntity(int connectionId, out NetworkEntityId id)
    {
        id = NetworkEntityId.Invalid;
        if (!_playersByConnection.TryGetValue(connectionId, out var player))
            return false;
        id = player.EntityId;
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
            if (Combat is not null && Combat.Get(player.EntityId).Health <= 0)
                player.Motion.Reset(player.Position, player.Position);
            player.Motion.Step(fixedDeltaSeconds);
            _spatial.Move(player.EntityId, player.Position);
            Combat?.Move(player.EntityId, player.Position);
            if (!player.Motion.IsMoving)
                _stoppedPlayers.Add(connectionId);
        }
        foreach (var connectionId in _stoppedPlayers)
            _movingPlayers.Remove(connectionId);
        // Resolve queued attacks after movement, on current authoritative positions; no client-time rewind.
        Npc?.Move(fixedDeltaSeconds, Tick);
        Combat?.Simulate(fixedDeltaSeconds, Tick);
        Abilities?.Simulate(fixedDeltaSeconds, Tick);
        Npc?.Resolve(fixedDeltaSeconds, Tick);
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
            var position = _playersByEntity.TryGetValue(id, out var player)
                ? player.Position : Combat!.Get(id).Position;
            if (!view.Visible.Contains(id) &&
                Vector2.DistanceSquared(observer.Position, position) > squaredEnterRadius)
                continue;
            if (view.Visible.Add(id))
                view.EnteredIds.Add(id);
            if (player is null)
            {
                if (Npc is { } npcActor && id == npcActor.Id)
                    view.States.Add(new(id, npcActor.Motion.Position, 0, npcActor.Motion.Target));
                continue;
            }
            view.States.Add(new EntitySnapshot(
                player.EntityId,
                player.Position,
                player.LastProcessedSequence,
                player.Target,
                Combat?.Get(player.EntityId).LastAbilitySequence ?? 0,
                player.Motion.IsDashing ? player.Motion.DashDestination : default,
                player.Motion.DashSpeed));
        }
    }
}
