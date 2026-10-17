using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using V2 = System.Numerics.Vector2;

namespace ProjectG.Tests.Navigation;

/// <summary>Real lossy UDP: accepted action stops a route and replay cannot resurrect it.</summary>
public partial class NavigationCancellationSmoke : Node
{
    private EntitySnapshot _snapshot;
    private NetworkClient? _network;
    private PlayerController? _player;
    private readonly List<MoveCommand> _moves = new();

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            world.AutoConnect = false;
            _network = world.GetNode<NetworkClient>("NetworkClient");
            _network.DevelopmentIdentityProfile = "navigation-smoke-" + Guid.NewGuid().ToString("N")[..12];
            _network.SimulateNetworkConditions = false;
            _network.SimulatedMinLatencyMs = 60;
            _network.SimulatedMaxLatencyMs = 90;
            _network.SimulatedPacketLossPercent = 3;
            _network.SnapshotReceived += Snapshot;
            _network.MovementSent += Movement;
            AddChild(world);
            _network.ConnectToServer();
            await Wait(() =>
            {
                _player = world.GetChildren().OfType<PlayerController>().FirstOrDefault(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
                return _network.RegionActive && _player is not null && _snapshot.EntityId == _player.EntityId;
            }, "spawn and authoritative baseline");
            // Test movement loss/jitter independently of the existing 30-second region-load deadline.
            _network.SimulateNetworkConditions = true;
            _network.ApplyNetworkSimulation();
            var start = _player!.PredictedPosition;
            MoveNearby(8);
            await Wait(() => V2.Distance(start, _snapshot.Position) > .2f, "ordinary movement");
            await Delay(1);
            Check(_moves.Count == 1 && _moves[0].Kind == MovementCommandKind.Destination,
                "One destination must not produce a per-tick movement stream");
            Check(_player.PredictionFrameCount < 20, "Sparse input acknowledgement did not prune replay history");
            Check(V2.Distance(_player.PredictedPosition, _snapshot.Position) < 1, "Sparse destination prediction drifted from authority");
            _network.SendAttack(new(1, _player.ClientTick, V2.UnitY));
            await Wait(() => _snapshot.CancelledNavigationOrder != 0 &&
                V2.Distance(_snapshot.Position, _snapshot.Target) < .001f, "accepted action cancels navigation");
            var stopped = _snapshot.Position;
            await Delay(1.2);
            Check(V2.Distance(stopped, _snapshot.Position) < .02f, "Old movement resumed on the server");
            Check(V2.Distance(_player.PredictedPosition, stopped) < .1f, "Client replay resurrected cancelled navigation");
            MoveNearby(2);
            await Wait(() => V2.Distance(stopped, _snapshot.Position) > .2f, "new explicit movement after the action");
            var previousCancellation = _snapshot.CancelledNavigationOrder;
            _network.SendAttack(new(2, _player.ClientTick, V2.UnitY, BasicAttackMode.BeginContinuous));
            await Wait(() => _snapshot.CancelledNavigationOrder != previousCancellation &&
                V2.Distance(_snapshot.Position, _snapshot.Target) < .001f, "continuous attack starts");
            var continuousCancellation = _snapshot.CancelledNavigationOrder;
            stopped = _snapshot.Position;
            MoveNearby(4);
            // Late holds may arrive after the newer movement. They must not restart repetition.
            for (uint sequence = 3; sequence < 8; sequence++)
            {
                _network.SendAttack(new(sequence, _player.ClientTick, V2.UnitY, BasicAttackMode.UpdateContinuous));
                await Delay(.1);
            }
            await Delay(1);
            Check(_snapshot.CancelledNavigationOrder == continuousCancellation, "Late continuous input cancelled the new route");
            Check(V2.Distance(stopped, _snapshot.Position) > .2f, "New movement did not cancel continuous repetition");
            _player.StopMovement();
            await Wait(() => _moves[^1].Kind == MovementCommandKind.Stop && _snapshot.LastProcessedSequence == _moves[^1].Sequence,
                "reliable server-position Stop");
            stopped = _snapshot.Position;
            await Delay(.5);
            Check(V2.Distance(stopped, _snapshot.Position) < .02f, "Stop retained an ordinary route");
            Check(V2.Distance(stopped, _player.PredictedPosition) < .1f, "Stop replay moved towards the old client stopping point");
            var cursorTarget = _player.PredictedPosition + V2.UnitY * 2;
            Check(_player.MoveCursorTo(cursorTarget, begin: true), "Cursor start rejected locally");
            await Wait(() => _snapshot.DirectCursor && _snapshot.RunningIntent, "direct cursor authority");
            for (var refresh = 0; refresh < 6; refresh++)
            { _player.MoveCursorTo(cursorTarget); await Delay(.05); }
            _player.ReleaseCursor(tap: false);
            await Wait(() => _moves[^1].Kind == MovementCommandKind.Stop && _snapshot.LastProcessedSequence == _moves[^1].Sequence,
                "release of held cursor stops authority");
            stopped = _snapshot.Position;
            await Delay(.5);
            Check(!_snapshot.DirectCursor && !_snapshot.RunningIntent && V2.Distance(stopped, _snapshot.Position) < .02f,
                "Released cursor retained movement or run-in-place intent");
            VerifyCorrectionThreshold();
            Detach();
            world.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("NAVIGATION_CANCELLATION_SMOKE_OK: UDP 60-90 ms / 3% loss, sparse Destination/Stop, bounded replay, cancellation, Continuous suppression, held direct cursor/release Stop, 0.4m soft / 0.6m hard correction.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void MoveNearby(float distance)
    {
        var player = _player!;
        foreach (var direction in new[] { V2.UnitY, -V2.UnitY, V2.UnitX, -V2.UnitX })
        {
            var target = player.PredictedPosition + direction * distance;
            var height = player.PredictedFoot.Y;
            if (player.Navigation.Surface is { } surface)
            {
                if (!surface.TryLocate(new(target.X, height, target.Y), 1, out var located)) continue;
                target = new(located.Position.X, located.Position.Z);
                height = located.Position.Y;
            }
            if (player.MoveTo(target, height)) return;
        }
        throw new InvalidOperationException("No nearby test route on the actual region surface");
    }

    private void Snapshot(WorldSnapshot snapshot)
    {
        foreach (var entity in snapshot.Entities)
            if (entity.EntityId == _player?.EntityId) _snapshot = entity;
    }
    private void Movement(MoveCommand command) => _moves.Add(command);

    private void VerifyCorrectionThreshold()
    {
        var player = _player!;
        player.SetPhysicsProcess(false);
        var foot = player.PredictedFoot;
        var direction = new[] { V2.UnitX, -V2.UnitX, V2.UnitY, -V2.UnitY }.First(d =>
            player.Navigation.TraverseSurface(foot, foot + new System.Numerics.Vector3(d.X, 0, d.Y)));
        var tick = _network!.LatestServerTick + 100;
        var baseline = _snapshot with { Position = player.PredictedPosition, Target = player.PredictedPosition,
            Height = foot.Y, TargetHeight = foot.Y, LastProcessedSequence = _moves[^1].Sequence,
            ProcessedClientTick = player.ClientTick + 100 };
        player.ApplySnapshot(baseline, tick++);
        player._PhysicsProcess(0);
        var rootHeight = MathF.Max(1, player.Navigation.AgentRadius);
        player.GlobalPosition = new(foot.X, foot.Y + rootHeight, foot.Z);
        var before = player.GlobalPosition;
        EntitySnapshot At(EntitySnapshot state, V2 position)
        {
            var height = foot.Y;
            if (player.Navigation.Surface is { } surface)
            {
                Check(surface.TryLocate(new(position.X, height, position.Y), .35f, out var point), "Synthetic correction left the surface");
                position = new(point.Position.X, point.Position.Z); height = point.Position.Y;
            }
            return state with { Position = position, Target = position, Height = height, TargetHeight = height };
        }
        var soft = At(baseline, baseline.Position + direction * .4f);
        player.ApplySnapshot(soft, tick++);
        player._PhysicsProcess(0);
        Check(player.GlobalPosition.DistanceTo(before) < .001f, "0.4m correction must preserve the visual position before smoothing");
        Check(V2.Distance(player.PredictedPosition, soft.Position) < .001f, "Soft correction did not restore authority internally");
        player._PhysicsProcess(.01);
        Check(player.GlobalPosition.DistanceTo(before) is > .001f and < .39f, "Soft correction must ease visually");
        var hard = At(soft, soft.Position + direction * .6f);
        player.ApplySnapshot(hard, tick);
        player._PhysicsProcess(0);
        var expected = new Vector3(hard.Position.X, hard.Height + rootHeight, hard.Position.Y);
        Check(player.GlobalPosition.DistanceTo(expected) < .001f, $"0.6m correction must snap to authority: actual={player.GlobalPosition}, expected={expected}");
    }
    private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> condition, string stage)
    {
        var deadline = Time.GetTicksMsec() + 45000;
        while (!condition())
        {
            if (Time.GetTicksMsec() >= deadline) throw new TimeoutException(stage);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private void Detach()
    {
        if (GodotObject.IsInstanceValid(_network))
        { _network!.SnapshotReceived -= Snapshot; _network.MovementSent -= Movement; }
        _network = null;
    }
    public override void _ExitTree() => Detach();
}
