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

    public override void _Ready()
    {
        _network = GetNode<NetworkClient>("NetworkClient");
        _network.PlayerSpawned += OnPlayerSpawned;
        _network.PlayerDespawned += OnPlayerDespawned;
        _network.SnapshotReceived += OnSnapshotReceived;
        _network.NavigationReceived += OnNavigationReceived;
        _network.CombatStateReceived += OnCombatState;
        _network.AttackReceived += OnAttack;
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
        _network.ProgressionReceived += OnProgression;
        _network.ProgressionResultReceived += OnProgressionResult;
        _network.EchoSpawned += OnEchoSpawn;
        _network.EchoLoadoutReceived += OnEchoLoadout;
        _network.EchoActionReceived += OnEchoAction;
        _network.EchoSignatureResultReceived += OnEchoResult;
        _network.ConnectToServer();
    }

    public override void _ExitTree()
    {
        if (_network is null)
            return;

        _network.PlayerSpawned -= OnPlayerSpawned;
        _network.PlayerDespawned -= OnPlayerDespawned;
        _network.SnapshotReceived -= OnSnapshotReceived;
        _network.NavigationReceived -= OnNavigationReceived;
        _network.CombatStateReceived -= OnCombatState;
        _network.AttackReceived -= OnAttack;
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
        _network.ProgressionReceived -= OnProgression;
        _network.ProgressionResultReceived -= OnProgressionResult;
        _network.EchoSpawned -= OnEchoSpawn;
        _network.EchoLoadoutReceived -= OnEchoLoadout;
        _network.EchoActionReceived -= OnEchoAction;
        _network.EchoSignatureResultReceived -= OnEchoResult;
    }

    private void OnNavigationReceived(NavigationGrid grid)
    {
        _navigationVisual?.QueueFree();
        _navigationVisual = new NavigationVisual { Name = "NavigationGeometry" };
        AddChild(_navigationVisual);
        _navigationVisual.Build(grid);
        foreach (var position in new Vector3[] { new(-9,0,-6),new(9,0,6) })
        {
            var marker = new Node3D { Position = position }; _navigationVisual.AddChild(marker);
            marker.AddChild(new MeshInstance3D { Position = new(0,0.15f,0), Mesh = new CylinderMesh { TopRadius = 1.4f, BottomRadius = 1.4f, Height = 0.1f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(0.1f,0.8f,0.7f) } });
            marker.AddChild(new Label3D { Position = new(0,1.5f,0), Text = "Открытие • EXP", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
        }
    }

    private void OnPlayerSpawned(PlayerSpawn spawn)
    {
        if (_players.ContainsKey(spawn.EntityId))
            return;

        var player = PlayerScene.Instantiate<PlayerController>();
        player.Name = $"Player-{spawn.EntityId.Value}";
        AddChild(player);
        player.Initialize(spawn, spawn.PlayerId == _network.LocalPlayerId, _network);
        _players.Add(spawn.EntityId, player);
        if (spawn.PlayerId == _network.LocalPlayerId)
        {
            _localEntityId = spawn.EntityId;
            _abilities = new AbilityPresentation();
            player.AddChild(_abilities);
            _abilities.Initialize(player, _network);
            _inventory = new InventoryPresentation(); AddChild(_inventory); _inventory.Initialize(_network);
            _echoControls = new(); AddChild(_echoControls); _echoControls.Initialize(_network);
            _progression = new(); AddChild(_progression); _progression.Initialize(_network);
        }
    }

    private void OnPlayerDespawned(PlayerDespawn despawn)
    {
        _combat.Remove(despawn.EntityId);
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
            actor.Position = new Vector3(state.Position.X, 1, state.Position.Y);
            if (actor is NpcPresentation npc) npc.Initialize(state, _network.Navigation!);
            actor.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = state.Kind == CombatEntityKind.Boss ? 0.7f : 0.4f,
                    BottomRadius = state.Kind == CombatEntityKind.Boss ? 0.7f : 0.4f, Height = 2 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = state.Kind == CombatEntityKind.Monster
                    ? new Color(0.9f, 0.15f, 0.1f) : state.Kind == CombatEntityKind.Boss
                        ? new Color(1, 0.5f, 0.05f) : new Color(0.65f, 0.3f, 0.8f) }
            });
            _targets.Add(state.EntityId, actor);
        }
        var presentation = new CombatPresentation { Name = "CombatPresentation" };
        actor.AddChild(presentation);
        presentation.Initialize(state, _network, state.EntityId == _localEntityId);
        _combat.Add(state.EntityId, presentation);
        RefreshAlive(state.EntityId);
    }

    private void OnAttack(AttackEvent action)
    {
        if (_combat.TryGetValue(action.AttackerId, out var actor))
            actor.Confirm(action);
        if (action.TargetId.IsValid && _combat.TryGetValue(action.TargetId, out var target))
            target.ApplyDamage(action);
        RefreshAlive(action.TargetId);
    }

    private void OnAttackResult(AttackResult result)
    {
        if (_combat.TryGetValue(_localEntityId, out var local))
            local.ApplyResult(result);
    }

    private void OnAbilityLoadout(AbilityLoadout value) => _abilities?.ApplyLoadout(value);
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
    }

    private void RefreshAlive(NetworkEntityId id)
    {
        if (id == _localEntityId && _combat.TryGetValue(id, out var local))
            { _inventory?.ApplyAlive(local.IsAlive); _progression?.ApplyAlive(local.IsAlive); }
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
    }

    private void ClearWorld()
    {
        foreach (var player in _players.Values)
            player.QueueFree();

        _players.Clear();
        foreach (var target in _targets.Values)
            target.QueueFree();
        _targets.Clear();
        _combat.Clear();
        foreach (var effect in _effects.Values) if (GodotObject.IsInstanceValid(effect)) effect.QueueFree();
        _effects.Clear(); _abilities = null;
        _inventory?.QueueFree(); _inventory = null;
        _progression?.QueueFree(); _progression = null;
        _groundItems?.QueueFree(); _groundItems = null;
        foreach (var echo in _echoes.Values) echo.QueueFree();
        _echoes.Clear(); _echoControls?.QueueFree(); _echoControls = null;
        _localEntityId = NetworkEntityId.Invalid;
        _navigationVisual?.QueueFree();
        _navigationVisual = null;
    }
    private void OnProgression(ProgressionState value)
    { if (value.OwnerId == _localEntityId) { _progression?.Apply(value); _abilities?.ApplyProgression(value); } }
    private void OnProgressionResult(ProgressionResult value) => _progression?.Result(value);
    private void OnEchoSpawn(EchoSpawn value)
    {
        if (_echoes.ContainsKey(value.EntityId)) return;
        var echo = new EchoPresentation(); AddChild(echo); echo.Initialize(value, _network.Navigation!); _echoes.Add(value.EntityId, echo);
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
