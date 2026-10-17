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
using Content.Server.Regions;

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
    private readonly RuntimeEntityAllocator _entityIds;
    internal RuntimeEntityAllocator EntityIds => _entityIds;
    internal ContentCatalog? RegionContent => _progressionCatalog;
    internal string? RegionPlayerProfile => _playerDefinition?.Id;
    public string RegionId { get; }
    public ulong GeometryHash { get; }
    private readonly Vector2 _spawn;
    private readonly float _spawnHeight;
    private readonly int _worldLayoutVersion;
    private readonly HashSet<int> _persistenceDirty = new();
    internal IReadOnlyCollection<int> PersistenceDirty => _persistenceDirty;

    public ServerWorld(IOptions<MovementOptions> options, IOptions<InterestOptions> interest,
        IOptions<NavigationOptions>? navigation = null, ContentCatalog? catalog = null,
        IOptions<CombatOptions>? combat = null, IOptions<ServerOptions>? server = null, IOptions<NpcOptions>? npc = null,
        IOptions<BossOptions>? boss = null, IOptions<InventoryOptions>? inventory = null,
        IOptions<GroundItemOptions>? groundItems = null, IOptions<EchoOptions>? echoes = null, IOptions<WorldStoryOptions>? worldStory = null, IOptions<StarterZoneOptions>? starterZone = null, IOptions<CraftingOptions>? crafting = null,
        string regionId = "prototype", RuntimeEntityAllocator? entityIds = null,
        Content.Server.WorldStory.WorldNodeDefinition? worldNodeDefinition = null,
        Content.Server.StarterZone.StarterZoneDefinition? starterZoneDefinition = null, IOptions<QuestOptions>? quests = null,
        Content.Server.Pvp.PvpDefinition? pvpDefinition = null, IReadOnlyDictionary<string,float[]>? scenePlacements = null,
        Func<double>? hitRoll = null, Func<double>? criticalRoll = null,IEchoCombatPermissionSource? echoPermissions=null)
    {
        RegionOwnership.ValidateRegion(regionId);
        ItemWear.Attach(ApplyEscrowWear);
        RegionId = regionId;
        _entityIds = entityIds ?? new RuntimeEntityAllocator();
        _movement = options.Value.ToSettings();
        _spawn = new(options.Value.SpawnX, options.Value.SpawnZ);
        _spawnHeight = options.Value.SpawnHeight;
        if (!float.IsFinite(_spawnHeight)) throw new ArgumentException("Spawn height must be finite.");
        _worldLayoutVersion = options.Value.WorldLayoutVersion;
        _pvpDefinition = pvpDefinition ?? catalog?.Pvp;
        _pvpDefinition?.Validate();
        _interest = interest.Value;
        if (!_interest.IsValid())
            throw new ArgumentException("Invalid interest settings.", nameof(interest));
        _spatial = new SpatialIndex(_interest.CellSize);
        Navigation = (navigation?.Value ?? new NavigationOptions()).CreateGrid(_movement);
        // Use the same pinned export for navigation, hash and the opening patch.
        if (navigation?.Value.LoadGeometry() is { } geometry)
        {
            GeometryHash = geometry.Hash;
            worldNodeDefinition = (worldNodeDefinition ?? catalog?.WorldNode ?? throw new InvalidDataException("Missing region rules."))
                with { OpeningCells = geometry.OpeningCells };
        }
        if (Navigation.Surface is not null) GeometryHash = Navigation.SurfaceHash;
        _pathfinder = new NavigationPathfinder(Navigation);
        if (catalog is not null)
        {
            var settings = combat?.Value ?? new CombatOptions();
            _playerDefinition = catalog.Creatures[settings.PlayerDefinitionId];
            _progressionCatalog = catalog;
            Combat = new CombatSimulation(catalog, _spatial, Navigation, settings,
                server?.Value.TickRate ?? NetworkConstants.ServerTickRate, _interest.CellSize, criticalRoll, hitRoll);
            Combat.CombatClock=()=>PvpClock()/1000d;
            Combat.PrepareActions=PrepareCombatActions;Combat.PrepareMotion=PrepareCombatMotion;
            Combat.MotionBoundary=NextCombatMotionBoundary;Combat.MotionFor=CurrentCombatMotion;
            Combat.ResolveWorldContacts=ResolveNpcContacts;
            Combat.FacingChanged=MarkPersistent;
            Combat.JournalVisibility=(observer,subject)=>Combat.TryGet(observer,out var a) && Combat.TryGet(subject,out var b) &&
                Vector2.DistanceSquared(a.Position,b.Position)<=_interest.Radius*_interest.Radius;
            Combat.Revived+=ActorRevived;
            Combat.CorpseExpired+=id=>Echoes?.OwnerCorpseRemoved(id);
            Combat.SupportPermission=(source,target)=>Social is not null && _pvpIdentities.TryGetValue(Combat.PrincipalActor(source),out var author) &&
                _pvpIdentities.TryGetValue(Combat.PrincipalActor(target),out var recipient) && Social.SameParty(author,recipient);
            var position = new Vector2(settings.TargetX, settings.TargetZ);
            if (!Navigation.IsOnSurface(new Vector3(position.X,settings.TargetHeight,position.Y)))
                throw new ArgumentException("Training target must have a walkable configured position.");
            TrainingTargetId = AllocateEntityId();
            Combat.Add(TrainingTargetId, position, CombatEntityKind.TrainingTarget,
                catalog.Swordsman is { } sword && sword.RegionId == regionId ? sword.DummyDefinitionId : null, height: settings.TargetHeight);
            _spatial.Add(TrainingTargetId, position);
            Abilities = new AbilitySimulation(catalog, Combat, _spatial, Navigation, settings, _interest.CellSize, StartDash);
            Combat.Defeated+=PlayerDefeated;
            Abilities.StopForCast = StopControlledMotion;
            Combat.StopForControl = StopControlledMotion;
            Combat.StartControlDash = StartControlledDash;
            Combat.FleeRequested=StartFleeRoute;
            Combat.InterruptRequested += id => { if(TryGetNpcActor(id,out var actor))actor.Interrupt(Tick,id); };
            Combat.CapabilityInterruptRequested+=(id,capability)=>
            {if(TryGetNpcActor(id,out var actor) && (actor.CurrentCapabilities&capability)!=0)actor.Interrupt(Tick,id);};
            if (inventory?.Value.Enabled == true)
            {
                Inventory = new InventorySimulation(catalog, Combat, Abilities, id => _playersByEntity[id].BaseStats);
                Inventory.UseWearLedger(ItemWear);
            }
            if (echoes?.Value.Enabled == true)
                Echoes = new EchoSimulation(echoes.Value,catalog,Navigation,_movement,Combat,Abilities,_spatial,()=>PvpClock(),Inventory,echoPermissions);
            if(Inventory is not null) { Combat.WeaponUsable=Inventory.WeaponUsable;
                Combat.SwordEquipped=id=>_playersByEntity.ContainsKey(id)?Inventory.HasEquippedSword(id):Combat.Get(id).Weapon.IsSword;
                Combat.BlockWeaponAvailable=id=>_playersByEntity.ContainsKey(id)?Inventory.HasFunctionalWeapon(id):Combat.Get(id).Weapon.Id!=catalog.UnarmedWeapon.Id; }
            if (groundItems?.Value.Enabled == true)
                GroundItems = new GroundItemSimulation(catalog, Inventory ?? throw new ArgumentException("Pickup requires inventory."),
                    Combat, Navigation, groundItems.Value, _interest.CellSize);
            if (npc?.Value is { Enabled: true } npcSettings)
            {
                if (!catalog.Creatures.ContainsKey(npcSettings.DefinitionId))
                    throw new ArgumentException("NPC creature definition is missing.");
                var id = AllocateEntityId();
                var home = new Vector2(npcSettings.X, npcSettings.Z);
                Combat.Add(id, home, CombatEntityKind.Monster, npcSettings.DefinitionId, height: npcSettings.Height);
                _spatial.Add(id, home);
                Abilities.AddActor(id, catalog.Creatures[npcSettings.DefinitionId]);
                Npc = new NpcSimulation(id, Combat, _spatial, Navigation, _movement, npcSettings, _interest.CellSize, abilities: Abilities);
            }
            if (boss?.Value is { Actor.Enabled: true } bossSettings)
            {
                var actor = bossSettings.Actor;
                if (!catalog.Creatures.TryGetValue(actor.DefinitionId, out var definition) ||
                    !definition.AbilityIds.Contains(bossSettings.AreaAbilityId) ||
                    !catalog.Abilities.TryGetValue(bossSettings.AreaAbilityId, out var area))
                    throw new ArgumentException("Boss must own a known area ability and creature profile.");
                var id = AllocateEntityId();
                var home = new Vector2(actor.X, actor.Z);
                Combat.Add(id, home, CombatEntityKind.Boss, actor.DefinitionId, height: actor.Height);
                _spatial.Add(id, home);
                Abilities.AddActor(id, definition);
                Boss = new NpcSimulation(id, Combat, _spatial, Navigation, _movement, actor, _interest.CellSize,
                    area, bossSettings, new StatCalculator(catalog.Balance), Abilities);
            }
        }
        InitializeWorldNode(worldStory?.Value,worldNodeDefinition ?? catalog?.WorldNode);
        InitializeStarterZone(starterZone?.Value,starterZoneDefinition ?? catalog?.StarterZone);
        InitializeCrafting(crafting?.Value,catalog?.Crafting); InitializePvp();
        InitializeQuests(quests?.Value.Enabled==true);
        InitializeSwordTraining(scenePlacements);
        InitializeNpcRespawns();
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

    public ServerPlayer AddPlayer(int connectionId, PlayerId playerId, CharacterState? saved = null,bool restoringFromLogout=true)
    {
        if (!playerId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(playerId));
        if (_playersByConnection.ContainsKey(connectionId))
            throw new ArgumentException("Connection already owns an entity.", nameof(connectionId));
        var entityId = AllocateEntityId();
        var spawnIndex = (int) ((entityId.Value - 1) % 10);
        var spawn = MovementSimulation.ClampTarget(_spawn + new Vector2(spawnIndex * 2f, 0f), _movement);
        if (!TryResolveSpawn(spawn, out spawn, out var spawnHeight))
            throw new InvalidOperationException("Region has no walkable spawn.");
        if (saved is not null)
        {
            saved.Validate();
            if (saved.RegionId != RegionId || _playerDefinition is null || saved.ProfileId != _playerDefinition.Id ||
                !Navigation.IsOnSurface(new Vector3(saved.X, saved.Surface?.Height ?? 0, saved.Z)) ||
                (Navigation.SurfaceHash != 0 && saved.Surface?.GeometryHash != Navigation.SurfaceHash))
                throw new InvalidDataException("Saved profile/position is incompatible with this region.");
            spawn = new(saved.X, saved.Z);
        }
        var player = new ServerPlayer(connectionId, playerId, entityId,
            new NavigationMover(Navigation, _movement, _pathfinder, spawn, saved?.Surface?.Height ?? spawnHeight));
        player.BaseStats = saved?.Stats ?? _playerDefinition?.Stats ?? default;
        player.RetainedEchoCombat=saved?.OwnedEchoes;
        player.RetainedEchoEffects=saved?.Effects?.Where(effect=>effect.TargetEcho is not null).ToArray() ?? [];
        _playersByConnection.Add(connectionId, player);
        _playersByEntity.Add(entityId, player);
        _spatial.Add(entityId, spawn);
        try
        {
            Combat?.Add(entityId, spawn, CombatEntityKind.Player, baseStats: player.BaseStats, height: player.Height);
            if (Abilities is not null && _playerDefinition is { } definition)
                Abilities.AddPlayer(entityId, definition);
            Inventory?.Add(entityId, saved?.Inventory ?? InventorySimulation.CreateStarter(_playerDefinition!),saved?.OfflineSeconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ?? 0);
            AddProgression(entityId,saved?.Progression);
            AddExploration(entityId);
            if (saved is not null)
            {
                var actor = Combat!.Get(entityId);
                var playerEffects=saved.Effects?.Where(effect=>effect.TargetEcho is null).ToArray();
                var previousStats=Combat.StatsAtSave(entityId,playerEffects,saved.ActivePassiveModifiers);
                if (saved.Health > previousStats.MaxHealth || saved.Mana>Math.Max(0,previousStats.MaxMana) || saved.Defense?.Stamina>Math.Max(0,previousStats.MaxStamina))
                    throw new InvalidDataException("Saved resources exceed previous maximum; content migration is required.");
                var offline = saved.OfflineSeconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                actor.Health=saved.Health;Combat.RestoreCombatPresence(entityId,saved.InCombatSeconds,offline);
                Combat.RestoreFacing(entityId,saved.Facing);
                Combat.RestoreEffects(entityId,playerEffects,offline,loggingIn:restoringFromLogout,savedPhase:saved.CombatControl?.Phase ?? CombatControlPhase.None);
                actor.Health = Math.Min(saved.Health,actor.Stats.MaxHealth);
                Combat.RefreshHealthRecovery(entityId);
                actor.ReadyAt = Combat.Time + Math.Max(0, saved.AttackCooldownSeconds - offline);
                Abilities!.Restore(entityId, saved with {Mana=Math.Min(saved.Mana,Math.Max(0,actor.Stats.MaxMana))}, offline);
                Combat.RestoreDefense(entityId,saved.Defense is {} defense ? defense with {Stamina=Math.Min(defense.Stamina,Math.Max(0,actor.Stats.MaxStamina))} : null,offline);
                Combat.RestoreControl(entityId,saved.CombatControl,offline,finishForcedMotion:restoringFromLogout);
            }
            AddPvp(entityId,saved?.Progression?.Pvp);
            Combat?.RestoreLife(entityId,saved?.Life,saved?.OfflineSeconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())??0,
                saved?.Progression?.Pvp);
            Combat?.RestoreJournal(entityId,saved?.DeathLogs,saved?.SessionJournal);
            if(saved?.DeathLogs is {Count:>0} logs && logs[^1].DeathId>Combat!.CaptureLife(entityId).DeathId)
                throw new InvalidDataException("Death journal ordinal exceeds character life.");
            Inventory?.FlushWear(entityId); // In-progress actions do not resume on restore; settle their durable debit before publication.
            Echoes?.Add(entityId,saved?.Echoes ?? SavedEchoes.Empty,saved?.OwnedEchoes,saved?.Effects,
                saved?.SavedAtUnixMilliseconds??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),AllocateEntityId,transfer:!restoringFromLogout);
        }
        catch { RemovePlayer(connectionId); throw; }
        return player;
    }

    public CharacterState CreateInitialCharacter()
    {
        if (_developmentBalanceCharacter is { } fixture)
            return fixture with { Inventory = fixture.Inventory! with { Items = fixture.Inventory.Items.Select(i => i with { InstanceId = Guid.NewGuid() }).ToArray() } };
        var definition = _playerDefinition ?? throw new InvalidOperationException("Persistence requires player content.");
        if (!TryResolveSpawn(_spawn, out var spawn, out var spawnHeight))
            throw new InvalidOperationException("Region has no spawn.");
        var stats = Combat!.InitialPlayerStats;
        return new CharacterState
        {
            RegionId = RegionId, ProfileId = definition.Id, Stats = definition.Stats, WorldLayoutVersion = _worldLayoutVersion,
            Inventory = Inventory is not null ? InventorySimulation.CreateStarter(definition) : null,
            // Companions require acquisition; login and legacy saves must never grant one.
            Echoes = SavedEchoes.Empty,
            Progression = SavedProgression.Starter(definition,_progressionCatalog!),
            X = spawn.X, Z = spawn.Y, Surface = Navigation.SurfaceHash == 0 ? null : new(1, spawnHeight, Navigation.SurfaceHash), Health = stats.MaxHealth, Mana = Math.Max(0, stats.MaxMana),
            AttackCooldownSeconds = 0, Cooldowns = definition.AbilityIds.Select(id => new SavedCooldown(id, 0)).ToArray(),
            Facing=new(1,0,1),
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public CharacterState CaptureCharacter(int connectionId,bool includeSessionJournal=true)
    {
        var player = _playersByConnection[connectionId];
        var actor = Combat!.Get(player.EntityId);
        return new CharacterState
        {
            RegionId = RegionId, ProfileId = _playerDefinition!.Id, Stats = player.BaseStats, WorldLayoutVersion = _worldLayoutVersion,
            Inventory = Inventory?.Capture(player.EntityId),
            Echoes = Echoes?.Capture(player.EntityId),
            OwnedEchoes = Echoes?.CaptureOwned(player.EntityId)??player.RetainedEchoCombat,
            Progression = CaptureProgression(player.EntityId),
            X = player.Position.X, Z = player.Position.Y, Surface = Navigation.SurfaceHash == 0 ? null : new(1, player.Height, Navigation.SurfaceHash), Health = actor.Health,
            Mana = Abilities!.Mana(player.EntityId), Cooldowns = Abilities.CaptureCooldowns(player.EntityId),
            AttackCooldownSeconds = Math.Max(0, actor.ReadyAt - Combat.Time),
            Facing=new(1,actor.Facing.X,actor.Facing.Y),
            InCombatSeconds=Combat.CombatRemaining(player.EntityId),
            Life=Combat.CaptureLife(player.EntityId),
            ActivePassiveModifiers=Combat.CapturePassiveModifiers(player.EntityId),
            Defense = Combat.CaptureDefense(player.EntityId),
            CombatControl = Combat.CaptureControl(player.EntityId),
            Effects = Combat.CaptureEffects(player.EntityId).Concat(Echoes?.CaptureEffects(player.EntityId)??player.RetainedEchoEffects).ToArray(),
            DeathLogs=Combat.DeathLogs(player.EntityId),SessionJournal=includeSessionJournal?Combat.SessionJournal(player.EntityId):null,
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public ServerPlayer? RemovePlayer(int connectionId, bool disconnectSocial = true)
    {
        if (!_playersByConnection.TryGetValue(connectionId, out var player))
            return null;
        if (disconnectSocial) DisconnectSocial(connectionId);
        else { socialPending.Remove(connectionId); SocialResults.Remove(connectionId); }
        GroundItems?.RemovePlayer(player.EntityId);
        Echoes?.Remove(player.EntityId);
        RemoveWorldNodePlayer(connectionId,player.EntityId);
        RemoveCraftPlayer(connectionId,player.EntityId);
        RemoveRepairPlayer(connectionId,player.EntityId);
        RemoveEconomyPlayer(connectionId,player.EntityId);
        RemoveTradePlayer(connectionId,player.EntityId);
        RemoveExploration(player.EntityId);
        RemovePvp(connectionId,player.EntityId); RemoveProgression(player.EntityId); ResetQuestSession(player.EntityId);
        _playersByConnection.Remove(connectionId);
        _developmentRevives.Remove(connectionId); _developmentReviveSequences.Remove(connectionId);
        _playersByEntity.Remove(player.EntityId);
        RemoveAvatar(player.EntityId); // NC: presentation leases never survive disconnect/region exit.
        ResetLogoutSession(connectionId);
        ResetLifeSession(connectionId);
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
            !player.Loaded || command.GeometryHash != Navigation.SurfaceHash || !float.IsFinite(command.TargetHeight) ||
            !MovementSimulation.IsValidTarget(command.Target, _movement))
        {
            return false;
        }

        if (Navigation.Surface is { } surface)
        {
            if (!surface.TryLocate(new(command.Target.X,command.TargetHeight,command.Target.Y),.35f,out var goal)) return false;
            command=command with { TargetHeight=goal.Position.Y };
        }
        Abilities?.CancelRecovery(player.EntityId);
        if (Combat?.IsRooted(player.EntityId) == true) return false;
        if (command.Target != player.Target || command.TargetHeight != player.Motion.TargetHeight)
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
            if (!player.Motion.TrySetTarget(command.Target, command.TargetHeight))
                return false;
        }
        player.LastProcessedSequence = command.Sequence;
        if (player.Motion.IsMoving)
        {
            Abilities?.NotifySustainAction(player.EntityId,Content.Shared.Combat.ToggleStopTriggers.Movement);
            _movingPlayers.Add(connectionId);
        }
        else
            _movingPlayers.Remove(connectionId);
        return true;
    }

    public bool TryQueueAttack(int connectionId, AttackCommand command) =>
        _playersByConnection.TryGetValue(connectionId, out var player) &&
        Combat?.Queue(player.EntityId, command, Tick) == true;
    public bool TryQueueBasicControl(int connectionId,BasicControlCommand command)=>
        _playersByConnection.TryGetValue(connectionId,out var player) && Combat?.QueueBasicControl(player.EntityId,command,Tick)==true;
    public bool TryQueueDefense(int connectionId, DefenseCommand command) =>
        _playersByConnection.TryGetValue(connectionId,out var player) && Combat?.QueueDefense(player.EntityId,command,Tick)==true;

    public bool TryQueueAbility(int connectionId, AbilityCommand command, int measuredRttMilliseconds) =>
        _playersByConnection.TryGetValue(connectionId, out var player) &&
        Abilities?.Queue(player.EntityId, command, Tick, measuredRttMilliseconds) == true;
    public bool IsToggleAbility(int connectionId,ushort skill)=>
        _playersByConnection.TryGetValue(connectionId,out var player) && Abilities?.IsToggleAbility(player.EntityId,skill)==true;
    public bool TryQueueActionCancel(int connectionId,ActionCancelCommand command) =>
        _playersByConnection.TryGetValue(connectionId,out var player) && Abilities?.QueueCancel(player.EntityId,command,Tick)==true;
    public bool TryQueueAbilityControl(int connectionId,AbilityControlCommand command)=>
        _playersByConnection.TryGetValue(connectionId,out var player) && Abilities?.QueueControl(player.EntityId,command,Tick)==true;
    public bool TryQueueInventory(int connectionId, InventoryCommand command) =>
        _playersByConnection.TryGetValue(connectionId, out var player) && Inventory?.Queue(player.EntityId, command, Tick) == true;
    public bool TryQueuePickup(int connectionId, PickupCommand command) =>
        _playersByConnection.TryGetValue(connectionId, out var player) && GroundItems?.Queue(player.EntityId, command, Tick) == true;
    public bool TryQueueEchoSignature(int connectionId, EchoSignatureCommand command) =>
        _playersByConnection.TryGetValue(connectionId,out var player) && Echoes?.Queue(player.EntityId,command,Tick) == true;
    public bool TryQueueEchoRoster(int connectionId,EchoRosterRequest command)=>
        _playersByConnection.TryGetValue(connectionId,out var player)&&player.Loaded&&Echoes?.QueueRoster(player.EntityId,command,Tick)==true;
    public bool TryQueueEchoSummon(int connectionId,EchoSummonCommand command)=>
        _playersByConnection.TryGetValue(connectionId,out var player)&&player.Loaded&&Echoes?.QueueSummon(player.EntityId,command,Tick)==true;
    public bool TryQueueEchoRecall(int connectionId,EchoRecallCommand command)=>
        _playersByConnection.TryGetValue(connectionId,out var player)&&player.Loaded&&Echoes?.QueueRecall(player.EntityId,command,Tick)==true;
    public bool TryQueueEchoSummonControl(int connectionId,EchoSummonControlCommand command)=>
        _playersByConnection.TryGetValue(connectionId,out var player)&&player.Loaded&&Echoes?.QueueSummonControl(player.EntityId,command,Tick)==true;
    public bool TryQueueEchoRestore(int connectionId,EchoRestoreCommand command)=>
        _playersByConnection.TryGetValue(connectionId,out var player)&&player.Loaded&&Echoes?.QueueRestore(player.EntityId,command,Tick)==true;
    public bool TryQueueEchoControl(int connectionId,EchoSignatureControlCommand command)=>
        _playersByConnection.TryGetValue(connectionId,out var player)&&player.Loaded&&Echoes?.QueueControl(player.EntityId,command,Tick)==true;
    private NetworkEntityId AllocateEntityId() => _entityIds.Allocate();
    public IReadOnlyList<Guid> PickupClaims(int connectionId) =>
        _playersByConnection.TryGetValue(connectionId, out var player) ? GroundItems?.Claims(player.EntityId) ?? [] : [];

    private bool StartDash(NetworkEntityId id, Vector2 destination, float speed)
    {
        return Combat is not null && Combat.TryGet(id, out var actor) && actor.Health > 0 &&
            StartControlledDash(id, destination, speed);
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
        SimulatePvpIntentions();
        SimulateRespawns();
        SimulateSocial();

        AttachPendingNpcActors();
        Echoes?.PrepareFrame(Tick);
        if(Combat is {} simulation)simulation.SimulateFrame(fixedDeltaSeconds,Tick,AdvanceCombatMotion);
        else AdvanceCombatMotion(fixedDeltaSeconds);
        RecordSwordTraining();
        AttachPendingNpcActors();
        if (Echoes is { } echoes)
        {
            echoes.Simulate(fixedDeltaSeconds,Tick);
            foreach (var owner in echoes.DirtyOwners) MarkPersistent(owner);
        }
        SimulateDeaths();
        Inventory?.FlushFinishedWear();
        ResetArenaDummies();
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
        SimulateQuests();
        SimulateEconomy();
        SimulateStarterZone();
        SimulateNpcRespawns();
        SimulateJournalRequests();
        SimulateAvatars(fixedDeltaSeconds); // NC: cosmetic requests run on the fixed tick after gameplay.
        if (Inventory is { } inventory) foreach (var id in inventory.Dirty) MarkPersistent(id);
        if (Combat is { } combat)
        {
            foreach (var id in combat.HealthRecovered) MarkPersistent(id);
            foreach (var id in combat.EffectDirty) MarkPersistent(id);
            foreach (var id in combat.CombatPresenceDirty) MarkPersistent(id);
            foreach (var id in combat.LifeDirty) MarkPersistent(id);
            foreach (var id in combat.DefenseDirty) MarkPersistent(id);
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
        SimulateLogout(); // Damage/control from this tick can still prevent a queued ordinary logout.
    }

    private void AdvanceCombatMotion(double delta)
    {
        _stoppedPlayers.Clear();
        // Moving players and NPC motion advance to each contact boundary.
        foreach(var connectionId in _movingPlayers)
        {
            var player=_playersByConnection[connectionId];
            _persistenceDirty.Add(connectionId);
            if(Combat is not null && Combat.Get(player.EntityId).Health<=0)player.Motion.Reset(player.Position,player.Position);
            var previousPosition=player.Position;
            var ordinary=!player.Motion.IsDashing && Combat?.IsFleeing(player.EntityId)!=true;
            player.Motion.Step((float)(delta*PlayerMovementMultiplier(player)));
            _spatial.Move(player.EntityId,player.Position);Combat?.Move(player.EntityId,player.Position,player.Height);
            if(ordinary)Combat?.FaceMovement(player.EntityId,player.Position-previousPosition);
            VisitDiscoveries(player);RevealStarterArea(player,player.Position!=previousPosition);Echoes?.Wake(player.EntityId);
            if(!player.Motion.IsMoving)_stoppedPlayers.Add(connectionId);
        }
        foreach(var connectionId in _stoppedPlayers)_movingPlayers.Remove(connectionId);
        foreach(var actor in _npcActors.Values)actor.Move(delta,Tick);
        Echoes?.Move(delta);
    }

    private void MarkPersistent(NetworkEntityId id)
    {
        if(Combat is not null)id=Combat.PrincipalActor(id);
        if (_playersByEntity.TryGetValue(id, out var player)) _persistenceDirty.Add(player.ConnectionId);
    }

    public PlayerSpawn CreateSpawn(ServerPlayer player) =>
        new(player.PlayerId, player.EntityId, player.Position, _movement, Tick, player.Height);

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
                ? player.Position : _questNpcs.TryGetValue(id,out var questNpc) ? questNpc.Position : Echoes?.TryGet(id,out var echo) == true ? echo.Motion.Position : Combat!.Get(id).Position;
            if (!view.Visible.Contains(id) &&
                Vector2.DistanceSquared(observer.Position, position) > squaredEnterRadius)
                continue;
            if (view.Visible.Add(id))
                view.EnteredIds.Add(id);
            if (player is null)
            {
                if (Echoes?.TryGet(id,out var echoActor) == true)
                    view.States.Add(ControlSnapshot(new(id,echoActor.Motion.Position,0,echoActor.Motion.Target,
                        DashDestination:echoActor.Motion.DashDestination,DashSpeed:echoActor.Motion.DashSpeed,
                        Height: echoActor.Motion.Height, TargetHeight: echoActor.Motion.TargetHeight,DashHeight:echoActor.Motion.DashHeight)));
                else if (TryGetNpcActor(id,out var npcActor))
                    view.States.Add(ControlSnapshot(new(id, npcActor.Motion.Position, 0, npcActor.Motion.Target, DashDestination: npcActor.Motion.DashDestination, DashSpeed: npcActor.Motion.DashSpeed, Height: npcActor.Motion.Height, TargetHeight: npcActor.Motion.TargetHeight, DashHeight: npcActor.Motion.DashHeight)));
                continue;
            }
            view.States.Add(ControlSnapshot(new EntitySnapshot(
                player.EntityId,
                player.Position,
                player.LastProcessedSequence,
                player.Target,
                Combat?.Get(player.EntityId).LastAbilitySequence ?? 0,
                player.Motion.IsDashing ? player.Motion.DashDestination : default,
                player.Motion.DashSpeed, player.Height, player.Motion.TargetHeight, player.Motion.DashHeight)));
        }
    }
}
