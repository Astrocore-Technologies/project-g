using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using V2 = System.Numerics.Vector2;

namespace ProjectG.Tests.Regions;

/// <summary>Isolated smoke test of the production world, picking and height snapshots.</summary>
public partial class LinvelBeechLiveSmoke : Node
{
    private WorldController _world = null!;
    private NetworkClient _network = null!;
    private PlayerController? _player;
    private EntitySnapshot _confirmed;

    public override async void _Ready()
    {
        try
        {
            _world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            _network = _world.GetNode<NetworkClient>("NetworkClient");
            _network.DevelopmentIdentityProfile = "beech-smoke-"+Guid.NewGuid().ToString("N")[..12];
            _network.SnapshotReceived += Snapshot;
            AddChild(_world);
            await Wait(() => _network.RegionActive && _network.CurrentRegion?.Region == "linvel_beech");
            _player = _world.GetChildren().OfType<PlayerController>().Single(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            await Wait(() => _confirmed.EntityId == _player.EntityId);
            var initial = _confirmed.Position;
            var goal = initial+new V2(7,-5);
            var worldPoint = ProjectG.Regions.PublicSurfaceAnchors.Position(_network,goal);
            var camera = GetViewport().GetCamera3D();
            _player._UnhandledInput(new InputEventMouseButton { ButtonIndex=MouseButton.Right,Pressed=true,
                Position=camera.UnprojectPosition(worldPoint) });
            await Wait(() => V2.Distance(_confirmed.Position,goal)<.3f);
            if (Math.Abs(_player.GlobalPosition.Y-(_confirmed.Height+1))>.3f)
                throw new InvalidOperationException("Rendered actor differs from server height.");
            if (!_player.MoveTo(initial,7.05f)) throw new InvalidOperationException("Return rejected.");
            await Wait(() => V2.Distance(_confirmed.Position,initial)<.15f);
            GD.Print("LINVEL_BEECH_LIVE_OK: authored region loaded, production RMB collider pick, authoritative movement and return.");
            _network.SnapshotReceived -= Snapshot;
            _world.QueueFree();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Snapshot(WorldSnapshot value)
    {
        if (_player is null) return;
        foreach (var state in value.Entities)
            if (state.EntityId == _player.EntityId) _confirmed=state;
    }

    private async Task Wait(Func<bool> done)
    {
        var end = Time.GetTicksMsec()+30000;
        while (!done())
        {
            if (Time.GetTicksMsec()>end)
                throw new TimeoutException($"Region={_network.CurrentRegion?.Region}, foot={_confirmed.Position}/{_confirmed.Height}, connection={_network.LastConnectionError}");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
