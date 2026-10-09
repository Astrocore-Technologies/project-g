using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using NumericsVector2 = System.Numerics.Vector2;

namespace ProjectG.Tests.Combat;

/// <summary>Exercises the real Space/cursor path and server reconciliation on a fresh non-Swordsman.</summary>
public partial class DashDistanceSmoke : Node
{
    private AbilityLoadout _loadout;
    private ProfessionState _profession;
    private EntitySnapshot _snapshot;
    private AbilityResult? _result;
    private NetworkClient? _network;
    private PlayerController? _player;

    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode = Window.ModeEnum.Windowed;
            GetWindow().Size = new(1280, 720);
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            world.AutoConnect = false;
            AddChild(world);
            _network = world.GetNode<NetworkClient>("NetworkClient");
            _network.AbilityLoadoutReceived += OnLoadout;
            _network.ProfessionReceived += OnProfession;
            _network.SnapshotReceived += OnSnapshot;
            _network.AbilityResultReceived += OnResult;
            _network.ConnectToServer();
            await Wait(() => _profession.OwnerId.IsValid && _loadout.Abilities is not null, "login");
            _player = world.GetChildren().OfType<PlayerController>()
                .Single(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            if (_profession.ActiveId != 0 || _loadout.Abilities.Single(a => a.Form == AbilityForm.Dash).Range != 3)
                throw new InvalidOperationException("Expected a fresh character with the ordinary 3 m dash.");

            var start = new NumericsVector2(19, -11);
            var cursorTarget = new NumericsVector2(11, -11);
            var destination = new NumericsVector2(16, -11);
            if (!_player.MoveTo(start)) throw new InvalidOperationException("No route to the sword ring.");
            await Wait(() => NumericsVector2.Distance(_player.PredictedPosition, start) < .1f &&
                NumericsVector2.Distance(_snapshot.Position, start) < .1f, "arrival at ring");
            await Delay(.4);
            await Capture("dash-distance-before");

            // Aim 8 m away using real input; the ordinary profile must cap prediction and server travel to 3 m.
            var cursor = GetViewport().GetCamera3D().UnprojectPosition(new(cursorTarget.X, 0, cursorTarget.Y));
            if (!GetViewport().GetVisibleRect().HasPoint(cursor)) throw new InvalidOperationException("Cursor target is off-screen.");
            Input.WarpMouse(cursor);
            await Delay(.1);
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Space, Keycode = Key.Space, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Space, Keycode = Key.Space, Pressed = false });
            await Wait(() => _result.HasValue, "Space result");
            if (_result!.Value.Outcome != AbilityOutcome.Accepted) throw new InvalidOperationException($"Dash rejected: {_result}");
            await Wait(() => _snapshot.LastAbilitySequence == _result.Value.Sequence && _snapshot.DashSpeed == 0 &&
                NumericsVector2.Distance(_snapshot.Position, destination) < .15f &&
                NumericsVector2.Distance(_player.PredictedPosition, destination) < .15f, "3 m range cap");
            await Delay(.4);
            await Capture("dash-distance-after");
            GD.Print($"DASH_DISTANCE_OK: ordinary dash, cursor 8 m away, actual travel {NumericsVector2.Distance(start, _snapshot.Position):F2} m; server and client agree.");
            Detach();
            world.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            await Capture("dash-distance-failure");
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private void OnLoadout(AbilityLoadout value) => _loadout = value;
    private void OnProfession(ProfessionState value) => _profession = value;
    private void OnResult(AbilityResult value) => _result = value;
    private void OnSnapshot(WorldSnapshot value)
    {
        foreach (var entity in value.Entities)
            if (entity.EntityId == _player?.EntityId) _snapshot = entity;
    }
    public override void _ExitTree() => Detach();
    private void Detach()
    {
        if (!GodotObject.IsInstanceValid(_network)) return;
        _network!.AbilityLoadoutReceived -= OnLoadout;
        _network.ProfessionReceived -= OnProfession;
        _network.SnapshotReceived -= OnSnapshot;
        _network.AbilityResultReceived -= OnResult;
        _network = null;
    }
    private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> done, string stage)
    {
        var deadline = Time.GetTicksMsec() + 30000;
        while (!done())
        {
            if (Time.GetTicksMsec() > deadline) throw new TimeoutException(stage);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private async Task Capture(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture")) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/" + name + ".png");
    }
}
