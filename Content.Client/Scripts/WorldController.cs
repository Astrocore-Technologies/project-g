using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using Content.Shared.Navigation;
using ProjectG.Navigation;
using ProjectG.Combat;

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
        _network.Disconnected += ClearWorld;
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
        _network.Disconnected -= ClearWorld;
    }

    private void OnNavigationReceived(NavigationGrid grid)
    {
        _navigationVisual?.QueueFree();
        _navigationVisual = new NavigationVisual { Name = "NavigationGeometry" };
        AddChild(_navigationVisual);
        _navigationVisual.Build(grid);
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
            _localEntityId = spawn.EntityId;
    }

    private void OnPlayerDespawned(PlayerDespawn despawn)
    {
        _combat.Remove(despawn.EntityId);
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
            actor = new Node3D { Name = $"TrainingTarget-{state.EntityId.Value}" };
            AddChild(actor);
            actor.Position = new Vector3(state.Position.X, 1, state.Position.Y);
            actor.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 2 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.65f, 0.3f, 0.8f) }
            });
            _targets.Add(state.EntityId, actor);
        }
        var presentation = new CombatPresentation { Name = "CombatPresentation" };
        actor.AddChild(presentation);
        presentation.Initialize(state, _network, state.EntityId == _localEntityId);
        _combat.Add(state.EntityId, presentation);
    }

    private void OnAttack(AttackEvent action)
    {
        if (_combat.TryGetValue(action.AttackerId, out var actor))
            actor.Confirm(action);
        if (action.TargetId.IsValid && _combat.TryGetValue(action.TargetId, out var target))
            target.ApplyDamage(action);
    }

    private void OnAttackResult(AttackResult result)
    {
        if (_combat.TryGetValue(_localEntityId, out var local))
            local.ApplyResult(result);
    }

    private void OnSnapshotReceived(WorldSnapshot snapshot)
    {
        foreach (var entity in snapshot.Entities)
        {
            if (_players.TryGetValue(entity.EntityId, out var player))
                player.ApplySnapshot(entity, snapshot.ServerTick);
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
        _localEntityId = NetworkEntityId.Invalid;
        _navigationVisual?.QueueFree();
        _navigationVisual = null;
    }
}
