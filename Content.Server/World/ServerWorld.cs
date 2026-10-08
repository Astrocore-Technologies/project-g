using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Combat;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.Persistence;
using Content.Server.Items;
using Content.Shared.Movement;
using Content.Shared.Network;
using Content.Shared.Navigation;
using Microsoft.Extensions.Options;

namespace Content.Server.World;

/// <summary>
/// Authoritative movement state for the current test region.
/// It is accessed only by the server polling thread.
/// </summary>
public sealed partial class ServerWorld
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
    private readonly HashSet<int> _persistenceDirty = new();
    internal IReadOnlyCollection<int> PersistenceDirty => _persistenceDirty;

    public ServerWorld(IOptions<MovementOptions> options, IOptions<InterestOptions> interest,
        IOptions<NavigationOptions>? navigation = null, ContentCatalog? catalog = null,
        IOptions<CombatOptions>? combat = null, IOptions<ServerOptions>? server = null, IOptions<NpcOptions>? npc = null,
        IOptions<BossOptions>? boss = null, IOptions<InventoryOptions>? inventory = null,
        IOptions<GroundItemOptions>? groundItems = null, IOptions<EchoOptions>? echoes = null, IOptions<WorldStoryOptions>? worldStory = null, IOptions<StarterZoneOptions>? starterZone = null, IOptions<CraftingOptions>? crafting = null)
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
            _progressionCatalog = catalog;
            Combat = new CombatSimulation(catalog, _spatial, Navigation, settings,
                server?.Value.TickRate ?? NetworkConstants.ServerTickRate, _interest.CellSize);
            var position = new Vector2(settings.TargetX, settings.TargetZ);
            if (!Navigation.IsWalkable(position))
                throw new ArgumentException("Training target must have a walkable configured position.");
            TrainingTargetId = new NetworkEntityId(_nextEntityId++);
            Combat.Add(TrainingTargetId, position, CombatEntityKind.TrainingTarget);
            _spatial.Add(TrainingTargetId, position);
            Abilities = new AbilitySimulation(catalog, Combat, _spatial, Navigation, settings, _interest.CellSize, StartDash);
            if (echoes?.Value.Enabled == true)
                Echoes = new EchoSimulation(echoes.Value,Navigation,_movement,Combat,_spatial,id =>
                { var owner = _playersByEntity[id]; return (owner.Position,Combat.Get(id).Health > 0,owner.Motion.IsMoving); });
            if (inventory?.Value.Enabled == true)
                Inventory = new InventorySimulation(catalog, Combat, Abilities, id => _playersByEntity[id].BaseStats);
            if(Inventory is not null) Combat.WeaponUsable=Inventory.WeaponUsable;
            if (groundItems?.Value.Enabled == true)
                GroundItems = new GroundItemSimulation(catalog, Inventory ?? throw new ArgumentException("Pickup requires inventory."),
                    Combat, Navigation, groundItems.Value, _interest.CellSize);
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
            if (boss?.Value is { Actor.Enabled: true } bossSettings)
            {
                var actor = bossSettings.Actor;
                if (!catalog.Creatures.TryGetValue(actor.DefinitionId, out var definition) ||
                    !definition.AbilityIds.Contains(bossSettings.AreaAbilityId) ||
                    !catalog.Abilities.TryGetValue(bossSettings.AreaAbilityId, out var area))
                    throw new ArgumentException("Boss must own a known area ability and creature profile.");
                var id = new NetworkEntityId(_nextEntityId++);
                var home = new Vector2(actor.X, actor.Z);
                Combat.Add(id, home, CombatEntityKind.Boss, actor.DefinitionId);
                _spatial.Add(id, home);
                Boss = new NpcSimulation(id, Combat, _spatial, Navigation, _movement, actor, _interest.CellSize,
                    area, bossSettings, new StatCalculator(catalog.Balance));
            }
        }
        InitializeWorldNode(worldStory?.Value,catalog?.WorldNode);
        InitializeStarterZone(starterZone?.Value,catalog?.StarterZone);
        InitializeCrafting(crafting?.Value,catalog?.Crafting);
    }

    public uint Tick { get; private set; }
    public NavigationGrid Navigation { get; }
    public CombatSimulation? Combat { get; }
    public AbilitySimulation? Abilities { get; }
    public NetworkEntityId TrainingTargetId { get; }
    public NpcSimulation? Npc { get; }
    public NpcSimulation? Boss { get; }
    public InventorySimulation? Inventory { get; }
    public GroundItemSimulation? GroundItems { get; }
    public EchoSimulation? Echoes { get; }
    public IReadOnlyCollection<ServerPlayer> Players => _playersByConnection.Values;

    public ServerPlayer AddPlayer(int connectionId, PlayerId playerId, CharacterState? saved = null)
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
        if (saved is not null)
        {
            saved.Validate();
            if (_playerDefinition is null || saved.ProfileId != _playerDefinition.Id ||
                !Navigation.IsWalkable(new Vector2(saved.X, saved.Z)))
                throw new InvalidDataException("Saved profile/position is incompatible with this region.");
            spawn = new(saved.X, saved.Z);
        }
        var player = new ServerPlayer(connectionId, playerId, entityId,
            new NavigationMover(Navigation, _movement, _pathfinder, spawn));
        player.BaseStats = saved?.Stats ?? _playerDefinition?.Stats ?? default;
        _playersByConnection.Add(connectionId, player);
        _playersByEntity.Add(entityId, player);
        _spatial.Add(entityId, spawn);
        try
        {
            Combat?.Add(entityId, spawn, CombatEntityKind.Player, baseStats: player.BaseStats);
            if (Abilities is not null && _playerDefinition is { } definition)
                Abilities.AddPlayer(entityId, definition);
            Inventory?.Add(entityId, saved?.Inventory ?? InventorySimulation.CreateStarter(_playerDefinition!),saved?.OfflineSeconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ?? 0);
            AddProgression(entityId,saved?.Progression);
            AddExploration(entityId);
            if (saved is not null)
            {
                var actor = Combat!.Get(entityId);
                if (saved.Health > actor.Stats.MaxHealth)
                    throw new InvalidDataException("Saved HP exceeds current maximum; content migration is required.");
                var offline = saved.OfflineSeconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                actor.Health = saved.Health;
                actor.ReadyAt = Combat.Time + Math.Max(0, saved.AttackCooldownSeconds - offline);
                Abilities!.Restore(entityId, saved, offline);
            }
            Echoes?.Add(entityId,saved?.Echoes ?? Echoes.CreateStarter(),saved?.OfflineSeconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ?? 0,AllocateEntityId);
        }
        catch { RemovePlayer(connectionId); throw; }
        return player;
    }

    public CharacterState CreateInitialCharacter()
    {
        var definition = _playerDefinition ?? throw new InvalidOperationException("Persistence requires player content.");
        if (!Navigation.TryFindSpawn(new Vector2(-9, 0), out var spawn))
            throw new InvalidOperationException("Region has no spawn.");
        var stats = Combat!.InitialPlayerStats;
        return new CharacterState
        {
            RegionId = "prototype", ProfileId = definition.Id, Stats = definition.Stats,
            Inventory = Inventory is not null ? InventorySimulation.CreateStarter(definition) : null,
            Echoes = Echoes?.CreateStarter(),
            Progression = SavedProgression.Starter(definition,_progressionCatalog!),
            X = spawn.X, Z = spawn.Y, Health = stats.MaxHealth, Mana = Math.Max(0, stats.MaxMana),
            AttackCooldownSeconds = 0, Cooldowns = definition.AbilityIds.Select(id => new SavedCooldown(id, 0)).ToArray(),
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public CharacterState CaptureCharacter(int connectionId)
    {
        var player = _playersByConnection[connectionId];
        var actor = Combat!.Get(player.EntityId);
        return new CharacterState
        {
            RegionId = "prototype", ProfileId = _playerDefinition!.Id, Stats = player.BaseStats,
            Inventory = Inventory?.Capture(player.EntityId),
            Echoes = Echoes?.Capture(player.EntityId),
            Progression = _progression.GetValueOrDefault(player.EntityId),
            X = player.Position.X, Z = player.Position.Y, Health = actor.Health,
            Mana = Abilities!.Mana(player.EntityId), Cooldowns = Abilities.CaptureCooldowns(player.EntityId),
            AttackCooldownSeconds = Math.Max(0, actor.ReadyAt - Combat.Time),
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public ServerPlayer? RemovePlayer(int connectionId)
    {
        if (!_playersByConnection.TryGetValue(connectionId, out var player))
            return null;
        GroundItems?.RemovePlayer(player.EntityId);
        Echoes?.Remove(player.EntityId);
        RemoveWorldNodePlayer(connectionId,player.EntityId);
        RemoveCraftPlayer(connectionId,player.EntityId);
        RemoveRepairPlayer(connectionId,player.EntityId);
        RemoveEconomyPlayer(connectionId,player.EntityId);
        RemoveTradePlayer(connectionId,player.EntityId);
        RemoveExploration(player.EntityId);
        RemoveProgression(player.EntityId);
        _playersByConnection.Remove(connectionId);
        _developmentRevives.Remove(connectionId); _developmentReviveSequences.Remove(connectionId);
        _playersByEntity.Remove(player.EntityId);
        _spatial.Remove(player.EntityId);
        _movingPlayers.Remove(connectionId);
        _persistenceDirty.Remove(connectionId);
        Combat?.Remove(player.EntityId);
        Abilities?.Remove(player.EntityId);
        Inventory?.Remove(player.EntityId);
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
    public bool TryQueueInventory(int connectionId, InventoryCommand command) =>
        _playersByConnection.TryGetValue(connectionId, out var player) && Inventory?.Queue(player.EntityId, command, Tick) == true;
    public bool TryQueuePickup(int connectionId, PickupCommand command) =>
        _playersByConnection.TryGetValue(connectionId, out var player) && GroundItems?.Queue(player.EntityId, command, Tick) == true;
    public bool TryQueueEchoSignature(int connectionId, EchoSignatureCommand command) =>
        _playersByConnection.TryGetValue(connectionId,out var player) && Echoes?.Queue(player.EntityId,command,Tick) == true;
    private NetworkEntityId AllocateEntityId()
    {
        if (_nextEntityId == 0) throw new InvalidOperationException("Runtime IDs exhausted.");
        return new(_nextEntityId++);
    }
    public IReadOnlyList<Guid> PickupClaims(int connectionId) =>
        _playersByConnection.TryGetValue(connectionId, out var player) ? GroundItems?.Claims(player.EntityId) ?? [] : [];

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
        _persistenceDirty.Clear();

        _stoppedPlayers.Clear();
        // Idle players do not require movement work or spatial updates each tick.
        foreach (var connectionId in _movingPlayers)
        {
            var player = _playersByConnection[connectionId];
            _persistenceDirty.Add(connectionId);
            if (Combat is not null && Combat.Get(player.EntityId).Health <= 0)
                player.Motion.Reset(player.Position, player.Position);
            var previousPosition=player.Position;
            player.Motion.Step(fixedDeltaSeconds);
            _spatial.Move(player.EntityId, player.Position);
            Combat?.Move(player.EntityId, player.Position);
            VisitDiscoveries(player);
            RevealStarterArea(player,player.Position!=previousPosition);
            Echoes?.Wake(player.EntityId);
            if (!player.Motion.IsMoving)
                _stoppedPlayers.Add(connectionId);
        }
        foreach (var connectionId in _stoppedPlayers)
            _movingPlayers.Remove(connectionId);
        // Resolve queued attacks after movement, on current authoritative positions; no client-time rewind.
        Npc?.Move(fixedDeltaSeconds, Tick);
        Boss?.Move(fixedDeltaSeconds, Tick);
        Combat?.Simulate(fixedDeltaSeconds, Tick);
        if(Inventory is not null) foreach(var action in Combat!.Events) Inventory.Wear(action);
        Abilities?.Simulate(fixedDeltaSeconds, Tick);
        Npc?.Resolve(fixedDeltaSeconds, Tick);
        Boss?.Resolve(fixedDeltaSeconds, Tick);
        Inventory?.Simulate(Tick);
        GroundItems?.Simulate(Tick);
        ApplyDevelopmentRevives();
        SimulateProgression();
        SimulateProfessions();
        SimulateWorldNode();
        SimulateResourceRefill();
        SimulateCrafting();
        SimulateRepair();
        SimulateTrade();
        SimulateEconomy();
        if (Echoes is { } echoes)
        {
            foreach (var action in Combat!.Events)
            {
                if (action.TargetId.IsValid) { echoes.Alert(action.AttackerId,action.TargetId); echoes.Alert(action.TargetId,action.AttackerId); }
            }
            foreach (var hit in Abilities!.Hits) { echoes.Alert(hit.ActorId,hit.TargetId); echoes.Alert(hit.TargetId,hit.ActorId); }
            echoes.Simulate(fixedDeltaSeconds,Tick);
            foreach (var owner in echoes.DirtyOwners) MarkPersistent(owner);
        }
        SimulateStarterZone();
        if (Inventory is { } inventory) foreach (var id in inventory.Dirty) MarkPersistent(id);
        if (Combat is { } combat)
        {
            foreach (var action in combat.Events)
            {
                MarkPersistent(action.AttackerId);
                MarkPersistent(action.TargetId);
            }
            // Misses can still consume a melee cooldown.
            foreach (var id in combat.Results.Keys) MarkPersistent(id);
        }
        if (Abilities is { } abilities)
        {
            foreach (var id in abilities.DirtyActors) MarkPersistent(id);
            foreach (var hit in abilities.Hits) MarkPersistent(hit.TargetId);
        }
    }

    private void MarkPersistent(NetworkEntityId id)
    {
        if (_playersByEntity.TryGetValue(id, out var player)) _persistenceDirty.Add(player.ConnectionId);
    }

    public PlayerSpawn CreateSpawn(ServerPlayer player) =>
        new(player.PlayerId, player.EntityId, player.Position, _movement, Tick);

    public PlayerSpawn CreateSpawn(NetworkEntityId id) => CreateSpawn(_playersByEntity[id]);

    public void UpdateGroundInterest(int connectionId, InterestView view)
    {
        view.GroundEntered.Clear(); view.GroundLeft.Clear();
        if (GroundItems is not { } ground) return;
        var position = _playersByConnection[connectionId].Position;
        ground.Query(position, _interest.ExitRadius, view.GroundCandidates);
        foreach (var id in view.GroundVisible)
            if (!view.GroundCandidates.Contains(id)) view.GroundLeft.Add(id);
        foreach (var id in view.GroundLeft) view.GroundVisible.Remove(id);
        foreach (var id in view.GroundCandidates)
            if (!view.GroundVisible.Contains(id) && Vector2.DistanceSquared(position, ground.State(id.Value, Tick).Position) <= _interest.Radius * _interest.Radius)
            { view.GroundVisible.Add(id); view.GroundEntered.Add(id); }
    }

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
                ? player.Position : Echoes?.TryGet(id,out var echo) == true ? echo.Motion.Position : Combat!.Get(id).Position;
            if (!view.Visible.Contains(id) &&
                Vector2.DistanceSquared(observer.Position, position) > squaredEnterRadius)
                continue;
            if (view.Visible.Add(id))
                view.EnteredIds.Add(id);
            if (player is null)
            {
                if (Echoes?.TryGet(id,out var echoActor) == true)
                    view.States.Add(new(id,echoActor.Motion.Position,0,echoActor.Motion.Target));
                else if (Npc is { } npcActor && id == npcActor.Id)
                    view.States.Add(new(id, npcActor.Motion.Position, 0, npcActor.Motion.Target));
                else if (Boss is { } bossActor && id == bossActor.Id)
                    view.States.Add(new(id, bossActor.Motion.Position, 0, bossActor.Motion.Target));
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
