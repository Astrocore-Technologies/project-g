using Godot;
using ProjectG.App;
using ProjectG.Gameplay;
using ProjectG.Networking;

namespace ProjectG.Tests.App;

/// <summary>Exercises actual menu/session lifecycle using an isolated Development server/profile.</summary>
public partial class MainMenuSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var live = OS.GetCmdlineUserArgs().Contains("--live");
            var menu = GD.Load<PackedScene>("res://Scenes/App/MainMenu.tscn").Instantiate<MainMenu>();
            AddChild(menu);
            await Frame();
            if (menu.IsConnecting || menu.IsInWorld || menu.GetChildren().OfType<WorldController>().Any())
                throw new InvalidOperationException("Menu connected before the player requested entry.");
            if (OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/main-menu.png");
            }
            if (!live) menu.LoginTimeoutSeconds = .25;
            menu.EnterWorld();
            menu.EnterWorld(); // A double click must not create two sessions.
            if (menu.GetChildren().OfType<WorldController>().Count() != 1)
                throw new InvalidOperationException("Entry created duplicate world sessions.");
            await WaitFor(() => live ? menu.IsInWorld : !menu.IsConnecting, 20000);
            if (!live)
            {
                await Frame();
                if (menu.IsInWorld || menu.GetChildren().OfType<WorldController>().Any() ||
                    !menu.StatusText.Contains("Сервер не ответил"))
                    throw new InvalidOperationException("Timed-out login did not release the world and return to menu.");
                menu.EnterWorld();
                await WaitFor(() => !menu.IsConnecting, 3000);
                await Frame();
                if (menu.GetChildren().OfType<WorldController>().Any())
                    throw new InvalidOperationException("Retry leaked a world session.");
            }
            else
            {
                var world = menu.GetChildren().OfType<WorldController>().Single();
                var network = world.GetNode<NetworkClient>("NetworkClient");
                if (network.Navigation is null || GetViewport().GetCamera3D() is null)
                    throw new InvalidOperationException("Menu opened gameplay before world/camera readiness.");
                var player = world.GetChildren().OfType<PlayerController>().Single(p =>
                    p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
                var position = player.PredictedPosition;
                // Force a local content rejection while polling is idle; the deferred cleanup path is identical.
                network.RejectRegionContent();
                await Frame();
                await Frame();
                if (menu.IsInWorld || menu.GetChildren().OfType<WorldController>().Any())
                    throw new InvalidOperationException("Disconnect left gameplay or networking alive.");
                await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
                menu.EnterWorld();
                await WaitFor(() => menu.IsInWorld, 20000);
                player = menu.GetChildren().OfType<WorldController>().Single().GetChildren()
                    .OfType<PlayerController>().Single(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
                if (System.Numerics.Vector2.Distance(player.PredictedPosition, position) > .2f)
                    throw new InvalidOperationException("Reconnect did not restore saved position.");
            }
            menu.QueueFree();
            await Frame();
            GD.Print(live ? "MAIN_MENU_LIVE_OK: enter, world readiness, disconnect, reconnect, persistence." :
                "MAIN_MENU_OFFLINE_OK: idle menu, duplicate entry guard, timeout cleanup, retry.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task WaitFor(Func<bool> condition, uint milliseconds)
    {
        var deadline = Time.GetTicksMsec() + milliseconds;
        while (!condition() && Time.GetTicksMsec() < deadline) await Frame();
        if (!condition()) throw new TimeoutException("Menu smoke deadline expired.");
    }

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
}
