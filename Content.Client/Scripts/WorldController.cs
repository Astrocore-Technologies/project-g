using Content.Shared.Network;
using Godot;
using ProjectG.Networking;

namespace ProjectG.Gameplay;

public partial class WorldController : Node3D
{
    [Export]
    public PackedScene PlayerScene { get; set; } = null!;

    private readonly Dictionary<NetworkEntityId, PlayerController> _players = new();
    private NetworkClient _network = null!;

    public override void _Ready()
    {
        _network = GetNode<NetworkClient>("NetworkClient");
        _network.PlayerSpawned += OnPlayerSpawned;
        _network.PlayerDespawned += OnPlayerDespawned;
        _network.SnapshotReceived += OnSnapshotReceived;
        _network.Disconnected += ClearPlayers;
        _network.ConnectToServer();
    }

    public override void _ExitTree()
    {
        if (_network is null)
            return;

        _network.PlayerSpawned -= OnPlayerSpawned;
        _network.PlayerDespawned -= OnPlayerDespawned;
        _network.SnapshotReceived -= OnSnapshotReceived;
        _network.Disconnected -= ClearPlayers;
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
    }

    private void OnPlayerDespawned(PlayerDespawn despawn)
    {
        if (!_players.Remove(despawn.EntityId, out var player))
            return;

        player.QueueFree();
    }

    private void OnSnapshotReceived(WorldSnapshot snapshot)
    {
        foreach (var entity in snapshot.Entities)
        {
            if (_players.TryGetValue(entity.EntityId, out var player))
                player.ApplySnapshot(entity, snapshot.ServerTick);
        }
    }

    private void ClearPlayers()
    {
        foreach (var player in _players.Values)
            player.QueueFree();

        _players.Clear();
    }
}
