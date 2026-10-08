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
    private AbilityPresentation? _abilities;
    private readonly Dictionary<ulong, AbilityEffectVisual> _effects = new();

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
        _network.AbilityLoadoutReceived -= OnAbilityLoadout;
        _network.AbilityResultReceived -= OnAbilityResult;
        _network.AbilityEffectReceived -= OnAbilityEffect;
        _network.AbilityHitReceived -= OnAbilityHit;
        _network.NpcWindupReceived -= OnNpcWindup;
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
        {
            _localEntityId = spawn.EntityId;
            _abilities = new AbilityPresentation();
            player.AddChild(_abilities);
            _abilities.Initialize(player, _network);
        }
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
            actor = state.Kind == CombatEntityKind.Monster ? new NpcPresentation() : new Node3D();
            actor.Name = $"{state.Kind}-{state.EntityId.Value}";
            AddChild(actor);
            actor.Position = new Vector3(state.Position.X, 1, state.Position.Y);
            if (actor is NpcPresentation npc) npc.Initialize(state, _network.Navigation!);
            actor.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 2 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = state.Kind == CombatEntityKind.Monster
                    ? new Color(0.9f, 0.15f, 0.1f) : new Color(0.65f, 0.3f, 0.8f) }
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
    private void OnAbilityHit(AbilityHit value)
    {
        if (_combat.TryGetValue(value.TargetId, out var target)) target.ApplyAbilityDamage(value);
    }

    private void RefreshAlive(NetworkEntityId id)
    {
        if (_players.TryGetValue(id, out var player) && _combat.TryGetValue(id, out var presentation))
            player.SetAlive(presentation.IsAlive);
    }
    private void OnNpcWindup(NpcWindup windup)
    {
        if (_combat.TryGetValue(windup.ActorId, out var presentation)) presentation.ShowWindup(windup);
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
        _localEntityId = NetworkEntityId.Invalid;
        _navigationVisual?.QueueFree();
        _navigationVisual = null;
    }
}
