using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;

namespace ProjectG.Tests.Regions;

/// <summary>Loads the real main scene and exercises the existing player's RMB input.</summary>
public partial class RiverLandingLiveSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            AddChild(world);
            var network = world.GetNode<NetworkClient>("NetworkClient");
            PlayerController? player = null;
            var deadline = Time.GetTicksMsec() + 20000;
            while (Time.GetTicksMsec() < deadline)
            {
                player = world.GetChildren().OfType<PlayerController>().FirstOrDefault(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
                if (player is not null && network.Navigation is { CellCount: 5120 }) break;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (player is null || network.CurrentRegion?.GeometryHash == 0 || network.Navigation?.CellCount != 5120)
                throw new InvalidOperationException("No local player on authored region.");
            if (world.FindChildren("Preview", "", true, false).Count != 0 || world.GetNode<Node3D>("Ground").Visible)
                throw new InvalidOperationException("Inspector/legacy terrain leaked into gameplay.");
            var target = new System.Numerics.Vector2(-10, 18);
            var confirmed = false;
            void Snapshot(WorldSnapshot snapshot)
            {
                foreach (var entity in snapshot.Entities)
                    if (entity.EntityId == player.EntityId && System.Numerics.Vector2.Distance(entity.Position, target) < .2f) confirmed = true;
            }
            network.SnapshotReceived += Snapshot;
            var camera = GetViewport().GetCamera3D();
            player._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true,
                Position = camera.UnprojectPosition(new(target.X, 0, target.Y)) });
            deadline = Time.GetTicksMsec() + 10000;
            while (!confirmed && Time.GetTicksMsec() < deadline) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            network.SnapshotReceived -= Snapshot;
            if (!confirmed) throw new InvalidOperationException("RMB movement was not confirmed by the server.");
            if (OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/river-live.png");
            }
            world.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("RIVER_LIVE_OK: actual main scene, existing avatar/camera, RMB prediction and server confirmation.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
