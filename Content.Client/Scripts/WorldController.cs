using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using Content.Shared.Navigation;
using ProjectG.Navigation;
using ProjectG.Combat;
using ProjectG.Items;
using ProjectG.Echoes;

namespace ProjectG.Gameplay;

public partial class WorldController : Node3D
{
    [Export]
    public PackedScene PlayerScene { get; set; } = null!;

    // NC: standalone world scenes retain automatic login; the menu starts it explicitly.
    [Export] public bool AutoConnect { get; set; } = true;
    public event Action? WorldReady;
    private bool _worldReady;

    private readonly Dictionary<NetworkEntityId, PlayerController> _players = new();
    private readonly Dictionary<NetworkEntityId, Node3D> _targets = new();
    private readonly Dictionary<NetworkEntityId, CombatPresentation> _combat = new();
    private NetworkEntityId _localEntityId;
    private NetworkClient _network = null!;
    private NavigationVisual? _navigationVisual;
    private AbilityPresentation? _abilities;
    private readonly Dictionary<ulong, AbilityEffectVisual> _effects = new();
    private InventoryPresentation? _inventory;
    private GroundItemPresentation? _groundItems;
    private readonly Dictionary<NetworkEntityId, EchoPresentation> _echoes = new();
    private EchoControls? _echoControls;
    private ProjectG.Progression.ProgressionPresentation? _progression;
    private ProjectG.UI.PlayerHud? _hud;
    private ProjectG.UI.GameNavigation? _uiNavigation;
    private ProjectG.Quests.QuestPresentation? _quests;
    private ProjectG.Progression.ProfessionPresentation? _profession;
    private ProjectG.WorldStory.WorldNodePresentation? _worldNode;
    private ProjectG.StarterZone.StarterZonePresentation? _starter;
    private ProjectG.Crafting.CraftingPresentation? _crafting;
    private ProjectG.Trading.TradePresentation? _trade;
    private ProjectG.Economy.EconomyPresentation? _economy;
    private ProjectG.Pvp.PvpPresentation? _pvp;
    private ProjectG.Social.SocialPresentation? _social;
    private readonly Dictionary<NetworkEntityId,Label3D> _pvpLabels=new();
    private Node3D? _safeVisual;

    public override void _Ready()
    {
        ProjectG.UI.GameUi.ModalOpened+=StopForMenu;
        _network = GetNode<NetworkClient>("NetworkClient");
        _network.PlayerSpawned += OnPlayerSpawned;
        _network.PlayerDespawned += OnPlayerDespawned;
        _network.SnapshotReceived += OnSnapshotReceived;
        _network.NavigationReceived += OnNavigationReceived;
        _network.CombatStateReceived += OnCombatState;
        _network.AttackReceived += OnAttack;
        _network.AvatarReceived += OnAvatar; // NC: skinned presentation for all visible players.
        _network.PvpPublicReceived+=OnPvpPublic;
        _network.AttackResultReceived += OnAttackResult;
        _network.AbilityLoadoutReceived += OnAbilityLoadout;
        _network.AbilityResultReceived += OnAbilityResult;
        _network.AbilityEffectReceived += OnAbilityEffect;
        _network.AbilityHitReceived += OnAbilityHit;
        _network.NpcWindupReceived += OnNpcWindup;
        _network.NpcAreaReceived += OnNpcArea;
        _network.InventoryReceived += OnInventory;
        _network.InventoryResultReceived += OnInventoryResult;
        _network.GroundItemSpawned += OnGroundSpawn;
        _network.GroundItemDespawned += OnGroundDespawn;
        _network.PickupResultReceived += OnPickupResult;
        _network.Disconnected += ClearWorld;
        _network.RegionChanged += OnRegionChanged;
        _network.WorldNodeReceived += OnWorldNode; _network.WorldNodeResultReceived += OnWorldNodeResult;
        _network.ProfessionReceived += OnProfession;
        _network.ProfessionResultReceived += OnProfessionResult;
        _network.ProgressionReceived += OnProgression;
        _network.ProgressionResultReceived += OnProgressionResult;
        _network.EchoSpawned += OnEchoSpawn;
        _network.EchoLoadoutReceived += OnEchoLoadout;
        _network.EchoActionReceived += OnEchoAction;
        _network.EchoSignatureResultReceived += OnEchoResult;
        if (AutoConnect) _network.ConnectToServer();
    }

    public override void _ExitTree()
    {
        ProjectG.UI.GameUi.ModalOpened-=StopForMenu;
        if (_network is null)
            return;

        _network.PlayerSpawned -= OnPlayerSpawned;
        _network.PlayerDespawned -= OnPlayerDespawned;
        _network.SnapshotReceived -= OnSnapshotReceived;
        _network.NavigationReceived -= OnNavigationReceived;
        _network.CombatStateReceived -= OnCombatState;
        _network.AttackReceived -= OnAttack;
        _network.AvatarReceived -= OnAvatar;
        _network.PvpPublicReceived-=OnPvpPublic;
        _network.AttackResultReceived -= OnAttackResult;
        _network.AbilityLoadoutReceived -= OnAbilityLoadout;
        _network.AbilityResultReceived -= OnAbilityResult;
        _network.AbilityEffectReceived -= OnAbilityEffect;
        _network.AbilityHitReceived -= OnAbilityHit;
        _network.NpcWindupReceived -= OnNpcWindup;
        _network.NpcAreaReceived -= OnNpcArea;
        _network.InventoryReceived -= OnInventory;
        _network.InventoryResultReceived -= OnInventoryResult;
        _network.GroundItemSpawned -= OnGroundSpawn;
        _network.GroundItemDespawned -= OnGroundDespawn;
        _network.PickupResultReceived -= OnPickupResult;
        _network.Disconnected -= ClearWorld;
        _network.RegionChanged -= OnRegionChanged;
        _network.WorldNodeReceived -= OnWorldNode; _network.WorldNodeResultReceived -= OnWorldNodeResult;
        _network.ProfessionReceived -= OnProfession;
        _network.ProfessionResultReceived -= OnProfessionResult;
        _network.ProgressionReceived -= OnProgression;
        _network.ProgressionResultReceived -= OnProgressionResult;
        _network.EchoSpawned -= OnEchoSpawn;
        _network.EchoLoadoutReceived -= OnEchoLoadout;
        _network.EchoActionReceived -= OnEchoAction;
        _network.EchoSignatureResultReceived -= OnEchoResult;
    }

    private void StopForMenu()
    {
        if(!_players.TryGetValue(_localEntityId,out var player)) return;
        player.StopMovement(); player.GetNodeOrNull<CombatPresentation>("CombatPresentation")?.CancelAutoAttack();
    }
    private void OnNavigationReceived(NavigationGrid grid)
    {
        if (grid.Surface is not null) { LoadSurfaceRegion(grid); if (_contentReady) _network.ConfirmRegionScene(); return; }
        if (!_contentReady) return;
        if (_network.CurrentRegion is not null) _network.ConfirmRegionScene();
        if (_authoredRegion is not null) { ApplyAuthoredNavigation(grid); return; }
        _navigationVisual?.QueueFree();
        _navigationVisual = new NavigationVisual { Name = "NavigationGeometry" };
        AddChild(_navigationVisual);
        _navigationVisual.Build(grid);

    }

    private void OnPlayerSpawned(PlayerSpawn spawn)
    {
        if (!_contentReady || _players.ContainsKey(spawn.EntityId))
            return;

        var player = PlayerScene.Instantiate<PlayerController>();
        player.Name = $"Player-{spawn.EntityId.Value}";
        AddChild(player);
        player.Initialize(spawn, spawn.PlayerId == _network.LocalPlayerId, _network);
        _players.Add(spawn.EntityId, player);
        if (spawn.PlayerId == _network.LocalPlayerId)
        {
            _localEntityId = spawn.EntityId;
            if(_network.LatestPvpZone is {} pvpZone){_social=new();AddChild(_social);_social.Initialize(_network,player);_pvp=new();AddChild(_pvp);_pvp.Initialize(_network,player);DrawSafeZone(pvpZone);}
            if(_network.CraftRecipes.Count!=0) { _crafting=new(); AddChild(_crafting); _crafting.Initialize(_network,player); _trade=new(); AddChild(_trade); _trade.Initialize(_network,player); _economy=new(); AddChild(_economy); _economy.Initialize(_network,player); }
            if (_network.LatestStarterZone is { } zone) { _starter=new(); AddChild(_starter); _starter.Initialize(_network,player,zone,_authoredRegion is null); }
            _abilities = new AbilityPresentation();
            player.AddChild(_abilities);
            _abilities.Initialize(player, _network);
            _inventory = new InventoryPresentation(); AddChild(_inventory); _inventory.Initialize(_network,_localEntityId);
            _echoControls = new(); AddChild(_echoControls); _echoControls.Initialize(_network,_localEntityId);
            _progression = new(); AddChild(_progression); _progression.Initialize(_network);
            // NC: preview the same modular appearance used by the world avatar.
            _progression.SetPreviewVisual(player.AppearanceScene);
            _hud = new(); AddChild(_hud); _hud.Initialize(_localEntityId,()=>_progression?.Toggle());
            _quests=new(); AddChild(_quests); _quests.Initialize(_network,player);
            var defense = new DefensePresentation(); player.AddChild(defense); defense.Initialize(player,_network,_hud);
            _profession = new(); AddChild(_profession); _profession.Initialize(_network);
            if(_network.LatestWorldNode is not null) { _worldNode=new(); AddChild(_worldNode); _worldNode.Initialize(_network,player); }
            ProjectG.UI.GameUi.ClearRoutes();
            ProjectG.UI.GameUi.RegisterRoute("character",()=>_progression?.Toggle());
            ProjectG.UI.GameUi.RegisterRoute("inventory",()=>_inventory?.Toggle());
            ProjectG.UI.GameUi.RegisterRoute("echo",()=>_echoControls?.Toggle());
            ProjectG.UI.GameUi.RegisterRoute("journal",()=>_quests?.OpenJournal());
            ProjectG.UI.GameUi.RegisterRoute("profession",()=>_profession?.Toggle());
            if(_starter is not null) { ProjectG.UI.GameUi.RegisterRoute("map",_starter.ToggleMap); ProjectG.UI.GameUi.RegisterRoute("guide",_starter.ToggleGuide); }
            if(_crafting is not null) ProjectG.UI.GameUi.RegisterRoute("craft",_crafting.Toggle);
            if(_trade is not null) ProjectG.UI.GameUi.RegisterRoute("trade",_trade.Toggle);
            if(_economy is not null) ProjectG.UI.GameUi.RegisterRoute("market",_economy.Toggle);
            if(_worldNode is not null) ProjectG.UI.GameUi.RegisterRoute("keeper",_worldNode.Toggle);
            if(_pvp is not null) ProjectG.UI.GameUi.RegisterRoute("pvp",_pvp.Toggle);
            if(_social is not null) { ProjectG.UI.GameUi.RegisterRoute("party",()=>_social.Toggle(SocialKind.Party)); ProjectG.UI.GameUi.RegisterRoute("clan",()=>_social.Toggle(SocialKind.Guild)); }
            _uiNavigation=new(); AddChild(_uiNavigation); _uiNavigation.Initialize(_network,_localEntityId);

            // NC: emotes use the same server-confirmed public stream for the owner and observers.
            var emotes = new ProjectG.Animation.EmoteMenu(); player.AddChild(emotes); emotes.Initialize(_network, player);

        }
    }

    private void OnPlayerDespawned(PlayerDespawn despawn)
    {
        _echoControls?.Remove(despawn.EntityId);
        _combat.Remove(despawn.EntityId);
        _pvpLabels.Remove(despawn.EntityId);
        if (_echoes.Remove(despawn.EntityId, out var echo)) echo.QueueFree();
        if (_players.Remove(despawn.EntityId, out var player))
            player.QueueFree();
        if (_targets.Remove(despawn.EntityId, out var target))
            target.QueueFree();
    }

    private void OnCombatState(CombatState state)
    {
        if (_combat.TryGetValue(state.EntityId, out var existing))
        {
            existing.ApplyState(state);
            RefreshAlive(state.EntityId);
            return;
        }
        Node3D actor;
        if (state.Kind == CombatEntityKind.Player)
        {
            if (!_players.TryGetValue(state.EntityId, out var player))
                return;
            actor = player;
        }
        else
        {
            actor = state.Kind is CombatEntityKind.Monster or CombatEntityKind.Boss ? new NpcPresentation() : new Node3D();
            actor.Name = $"{state.Kind}-{state.EntityId.Value}";
            AddChild(actor);
            actor.Position = new Vector3(state.Position.X, state.Height + 1, state.Position.Y);
            if (actor is NpcPresentation npc) npc.Initialize(state, _network.Navigation!);
            if (state.Kind == CombatEntityKind.TrainingTarget)
            {
                var dummy = GD.Load<PackedScene>("res://Scenes/Actors/TrainingDummy.tscn").Instantiate<Node3D>();
                dummy.Position = Vector3.Down; actor.AddChild(dummy);
            }
            else actor.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = state.Kind == CombatEntityKind.Boss ? 0.7f : 0.4f,
                    BottomRadius = state.Kind == CombatEntityKind.Boss ? 0.7f : 0.4f, Height = 2 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = state.Kind == CombatEntityKind.Monster
                    ? new Color(0.9f, 0.15f, 0.1f) : state.Kind == CombatEntityKind.Boss
                        ? new Color(1, 0.5f, 0.05f) : new Color(0.65f, 0.3f, 0.8f) }
            });
            _targets.Add(state.EntityId, actor);
            // NC: NPCs and training dummies also provide spatial contact feedback.
            actor.AddChild(new ProjectG.Animation.CharacterFeedback { Name="ContactFeedback", Position=new(0,-1,0) });
        }
        var presentation = new CombatPresentation { Name = "CombatPresentation" };
        actor.AddChild(presentation);
        presentation.Initialize(state, _network, state.EntityId == _localEntityId);
        _combat.Add(state.EntityId, presentation);
        RefreshAlive(state.EntityId);
    }

    private void OnPvpPublic(PvpPublicState s)
    {
        if (s.EntityId == _localEntityId) foreach (var gate in _gateLabels) { gate.Text = s.Tagged ? "Переход закрыт: действует метка боя" : "Выход в другую локацию"; gate.Modulate = s.Tagged ? Colors.Orange : Colors.White; }
        if(!_players.TryGetValue(s.EntityId,out var player))return;
        if(!_pvpLabels.TryGetValue(s.EntityId,out var label)){label=new Label3D(){Position=new Vector3(0,2.7f,0),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,FontSize=24};player.AddChild(label);_pvpLabels[s.EntityId]=label;}
        label.Text=s.Dead?"Погиб":s.Aggressor?"Агрессор":s.Protected?"Защита":s.Tagged?"PvP-тег":s.Mode==PvpMode.Peaceful?"":"PvP";label.Modulate=s.Aggressor?Colors.Red:s.Tagged?Colors.Orange:Colors.LightGreen;
    }
    private void DrawSafeZone(PvpZoneState z)
    {
        _safeVisual?.QueueFree();_safeVisual=new Node3D();AddChild(_safeVisual);var material=new StandardMaterial3D(){AlbedoColor=new Color(.2f,.9f,.5f)};
        var width=z.MaxX-z.MinX;var depth=z.MaxZ-z.MinZ;
        foreach(var edge in new[]{(new Vector3((z.MinX+z.MaxX)/2,.06f,z.MinZ),new Vector3(width,.06f,.08f)),(new Vector3((z.MinX+z.MaxX)/2,.06f,z.MaxZ),new Vector3(width,.06f,.08f)),(new Vector3(z.MinX,.06f,(z.MinZ+z.MaxZ)/2),new Vector3(.08f,.06f,depth)),(new Vector3(z.MaxX,.06f,(z.MinZ+z.MaxZ)/2),new Vector3(.08f,.06f,depth))})_safeVisual.AddChild(new MeshInstance3D(){Position=edge.Item1,Mesh=new BoxMesh(){Size=edge.Item2},MaterialOverride=material});
    }
    private void OnAttack(AttackEvent action)
    {
        if (_combat.TryGetValue(action.AttackerId, out var actor))
            actor.Confirm(action);
        if (action.TargetId.IsValid && _combat.TryGetValue(action.TargetId, out var target))
            target.ApplyDamage(action);
        RefreshAlive(action.TargetId);
        if(action.Damage > 0 || action.Guard != GuardImpact.None)
        {
            if (_players.TryGetValue(action.TargetId, out var victim)) victim.Animator.Hit(new Vector3(action.Direction.X,0,action.Direction.Y), action.Guard);
            else if(_targets.TryGetValue(action.TargetId,out var npc)) npc.GetNodeOrNull<ProjectG.Animation.CharacterFeedback>("ContactFeedback")?.Impact(action.Guard);
        }
    }

    private void OnAttackResult(AttackResult result)
    {
        if (_combat.TryGetValue(_localEntityId, out var local))
            local.ApplyResult(result);
    }

    private void OnAbilityLoadout(AbilityLoadout value) { _abilities?.ApplyLoadout(value); _hud?.Apply(value); }
    private void OnAbilityResult(AbilityResult value) => _abilities?.ApplyResult(value);
    private void OnInventory(InventoryState value)
    {
        if (value.EntityId == _localEntityId) _inventory?.Apply(value);
    }
    private void OnInventoryResult(InventoryResult value) => _inventory?.Result(value);
    private void OnGroundSpawn(GroundItemSpawn value)
    {
        if (_groundItems is null) { _groundItems = new(); AddChild(_groundItems); _groundItems.Initialize(_network); }
        _groundItems.Spawn(value);
    }
    private void OnGroundDespawn(GroundItemDespawn value) => _groundItems?.Despawn(value);
    private void OnPickupResult(PickupResult value) { _groundItems?.Result(value); _inventory?.PickupResult(value); }
    private void OnAbilityHit(AbilityHit value)
    {
        if (_combat.TryGetValue(value.TargetId, out var target)) target.ApplyAbilityDamage(value);
        RefreshAlive(value.TargetId);
        if (_players.TryGetValue(value.TargetId, out var victim) && (value.Damage > 0 || value.Guard != GuardImpact.None))
            victim.Animator.Hit(Vector3.Forward, value.Guard);
        else if((value.Damage > 0 || value.Guard != GuardImpact.None) && _targets.TryGetValue(value.TargetId,out var npc))
            npc.GetNodeOrNull<ProjectG.Animation.CharacterFeedback>("ContactFeedback")?.Impact(value.Guard);
    }

    private void OnAvatar(AvatarState value)
    { if (_players.TryGetValue(value.EntityId, out var player)) player.Animator.Apply(value); }

    private void RefreshAlive(NetworkEntityId id)
    {
        if (id == _localEntityId && _combat.TryGetValue(id,out var hudOwner)) _hud?.Health(hudOwner.Health,hudOwner.MaxHealth);
        if (id == _localEntityId && _combat.TryGetValue(id, out var local))
            { _inventory?.ApplyAlive(local.IsAlive); _progression?.ApplyAlive(local.IsAlive); _profession?.ApplyAlive(local.IsAlive); _worldNode?.ApplyAlive(local.IsAlive); }
        if (id == _localEntityId && _combat.TryGetValue(id, out var owner)) _echoControls?.ApplyAlive(owner.IsAlive);
        if (_players.TryGetValue(id, out var player) && _combat.TryGetValue(id, out var presentation))
            player.SetAlive(presentation.IsAlive);
    }
    private void OnNpcWindup(NpcWindup windup)
    {
        if (_combat.TryGetValue(windup.ActorId, out var presentation)) presentation.ShowWindup(windup);
    }
    private void OnNpcArea(NpcArea value)
    {
        if (_combat.TryGetValue(value.ActorId, out var presentation)) presentation.ShowArea(value);
    }

    private void OnAbilityEffect(AbilityEffectState value)
    {
        _abilities?.Confirm(value);
        if (_players.TryGetValue(value.ActorId, out var animated) && animated.IsAlive)
            animated.SwordAnimation.ApplyAbility(value);
        if (value.Phase == AbilityPhase.Finished)
        {
            if (_effects.Remove(value.EffectId, out var old) && GodotObject.IsInstanceValid(old)) old.QueueFree();
            return;
        }
        if (!_players.ContainsKey(value.ActorId)) return;
        if (!_effects.TryGetValue(value.EffectId, out var visual) || !GodotObject.IsInstanceValid(visual))
        {
            // Bounded presentation cache; reliable lifecycle plus TTL provides cleanup.
            if (_effects.Count >= 1024) return;
            visual = new AbilityEffectVisual(); AddChild(visual);
            _effects[value.EffectId] = visual;
        }
        visual.Apply(value);
    }

    private void OnSnapshotReceived(WorldSnapshot snapshot)
    {
        foreach (var entity in snapshot.Entities)
        {
            if (_players.TryGetValue(entity.EntityId, out var player))
                player.ApplySnapshot(entity, snapshot.ServerTick);
            else if (_targets.TryGetValue(entity.EntityId, out var actor) && actor is NpcPresentation npc)
                npc.Apply(entity, snapshot.ServerTick);
            else if (_echoes.TryGetValue(entity.EntityId, out var echo)) echo.Apply(entity, snapshot.ServerTick);
        }
        // NC: wait for the local avatar, validated navigation and its first authoritative snapshot.
        if (!_worldReady && _contentReady && _network.Navigation is not null &&
            _players.ContainsKey(_localEntityId))
        {
            foreach (var entity in snapshot.Entities)
                if (entity.EntityId == _localEntityId)
                {
                    _worldReady = true;
                    WorldReady?.Invoke();
                    break;
                }
        }
    }

    private void ClearWorld()
    {
        ProjectG.UI.GameUi.ClearRoutes(); DetachPresentation(_uiNavigation); _uiNavigation=null;
        DetachPresentation(_quests); _quests=null;
        _worldReady = false; // NC: a new region/session must become ready independently.
        UnloadAuthoredRegion();
        DetachPresentation(_regionExit); _regionExit = null;
        foreach (var player in _players.Values)
            DetachPresentation(player);

        _players.Clear();
        foreach (var target in _targets.Values)
            DetachPresentation(target);
        _targets.Clear();
        _combat.Clear();
        foreach (var effect in _effects.Values) if (GodotObject.IsInstanceValid(effect)) DetachPresentation(effect);
        _effects.Clear(); _abilities = null;
        DetachPresentation(_inventory); _inventory = null;
        DetachPresentation(_progression); _progression = null;
        DetachPresentation(_hud); _hud = null;
        DetachPresentation(_profession); _profession = null;
        DetachPresentation(_worldNode); _worldNode=null;
        DetachPresentation(_starter); _starter=null;
        DetachPresentation(_crafting); _crafting=null;
        DetachPresentation(_trade); _trade=null;
        DetachPresentation(_economy); _economy=null;
        DetachPresentation(_social);_social=null;
        DetachPresentation(_pvp);_pvp=null;_pvpLabels.Clear();DetachPresentation(_safeVisual);_safeVisual=null;
        DetachPresentation(_groundItems); _groundItems = null;
        foreach (var echo in _echoes.Values) DetachPresentation(echo);
        _echoes.Clear(); DetachPresentation(_echoControls); _echoControls = null;
        _localEntityId = NetworkEntityId.Invalid;
        DetachPresentation(_navigationVisual);
        _navigationVisual = null;
    }
    private void OnWorldNode(WorldNodeState value) => _worldNode?.Apply(value);
    private void OnWorldNodeResult(WorldNodeResult value) => _worldNode?.Result(value);
    private void OnProfession(ProfessionState value) { if (value.OwnerId == _localEntityId) _profession?.Apply(value); }
    private void OnProfessionResult(ProfessionResult value) => _profession?.Result(value);
    private void OnProgression(ProgressionState value)
    { if (value.OwnerId == _localEntityId) { _progression?.Apply(value); _abilities?.ApplyProgression(value); _hud?.Apply(value); } }
    private void OnProgressionResult(ProgressionResult value) => _progression?.Result(value);
    private void OnEchoSpawn(EchoSpawn value)
    {
        if (_echoes.ContainsKey(value.EntityId)) return;
        var echo = new EchoPresentation(); AddChild(echo); echo.Initialize(value, _network.Navigation!); _echoes.Add(value.EntityId, echo);
        var mesh=echo.GetChildren().OfType<MeshInstance3D>().First(); _echoControls?.Observe(value,mesh.Mesh,mesh.MaterialOverride);
    }
    private void OnEchoLoadout(EchoLoadout value) { if (value.OwnerId == _localEntityId) _echoControls?.Apply(value); }
    private void OnEchoResult(EchoSignatureResult value) => _echoControls?.Result(value);
    private void OnEchoAction(EchoAction value)
    {
        if (_echoes.TryGetValue(value.EntityId, out var echo)) echo.Show(value);
        if (value.TargetId.IsValid && _combat.TryGetValue(value.TargetId, out var target))
            target.ApplyDamage(new(value.EntityId, 0, value.ServerTick, value.Position, default, value.Radius, value.TargetId, value.Damage, value.TargetHealth, false));
        RefreshAlive(value.TargetId);
    }
}
