using Content.Shared.Movement;
using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using NumericsVector2 = System.Numerics.Vector2;

namespace ProjectG.Gameplay;

public partial class PlayerController : CharacterBody3D
{
    private const double RemoteInterpolationDelaySeconds = 0.1;
    private const int MaxPredictionHistory = 128;
    private const int MaxRemoteSnapshots = 20;

    private readonly List<PredictedFrame> _predictionHistory = new();
    private readonly List<RemotePoint> _remotePoints = new();

    private NetworkClient? _network;
    private MovementSettings _settings;
    private NumericsVector2 _predictedPosition;
    private NumericsVector2 _target;
    private double _tickAccumulator;
    private float _fixedDelta;
    private uint _sequence;
    private uint _clientTick;
    private bool _isLocal;

    public NetworkEntityId EntityId { get; private set; } = NetworkEntityId.Invalid;

    public void Initialize(
        PlayerSpawn spawn,
        bool isLocal,
        NetworkClient network)
    {
        EntityId = spawn.EntityId;
        _isLocal = isLocal;
        _network = network;
        _settings = spawn.Movement;
        _fixedDelta = 1f / network.ServerTickRate;
        _predictedPosition = spawn.Position;
        _target = spawn.Position;
        GlobalPosition = ToGodot(spawn.Position);

        var camera = GetNode<Camera3D>("CameraRig/Camera3D");
        camera.Current = isLocal;

        var collision = GetNode<CollisionShape3D>("CollisionShape3D");
        collision.Disabled = !isLocal;

        var material = new StandardMaterial3D
        {
            AlbedoColor = isLocal
                ? new Color(0.2f, 0.8f, 0.35f)
                : new Color(0.9f, 0.45f, 0.2f)
        };
        GetNode<MeshInstance3D>("MeshInstance3D").MaterialOverride = material;

        if (!isLocal)
            _remotePoints.Add(new RemotePoint(NowSeconds(), spawn.Position));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isLocal ||
            @event is not InputEventMouseButton mouseEvent ||
            mouseEvent.ButtonIndex != MouseButton.Left ||
            !mouseEvent.Pressed)
        {
            return;
        }

        var camera = GetViewport().GetCamera3D();
        if (camera is null)
            return;

        var rayOrigin = camera.ProjectRayOrigin(mouseEvent.Position);
        var rayDirection = camera.ProjectRayNormal(mouseEvent.Position);
        if (Mathf.Abs(rayDirection.Y) < 0.0001f)
            return;

        var distance = -rayOrigin.Y / rayDirection.Y;
        if (distance <= 0f)
            return;

        var clicked = rayOrigin + rayDirection * distance;
        _target = MovementSimulation.ClampTarget(
            new NumericsVector2(clicked.X, clicked.Z),
            _settings);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isLocal)
            UpdateLocal(delta);
        else
            UpdateRemote();
    }

    public void ApplySnapshot(EntitySnapshot snapshot, uint serverTick)
    {
        if (_isLocal)
        {
            Reconcile(snapshot);
            return;
        }

        _remotePoints.Add(new RemotePoint(NowSeconds(), snapshot.Position));
        if (_remotePoints.Count > MaxRemoteSnapshots)
            _remotePoints.RemoveAt(0);
    }

    private void UpdateLocal(double delta)
    {
        _tickAccumulator += delta;
        var simulatedTicks = 0;

        while (_tickAccumulator >= _fixedDelta && simulatedTicks++ < 4)
        {
            _tickAccumulator -= _fixedDelta;
            _clientTick++;
            _sequence++;
            if (_sequence == 0)
                _sequence++;

            var command = new MoveCommand(_sequence, _clientTick, _target);
            _predictedPosition = MovementSimulation.Step(
                _predictedPosition,
                command.Target,
                _settings,
                _fixedDelta);

            _predictionHistory.Add(new PredictedFrame(command));
            if (_predictionHistory.Count > MaxPredictionHistory)
                _predictionHistory.RemoveAt(0);

            _network?.SendMove(command);
        }

        var desired = ToGodot(_predictedPosition);
        var error = GlobalPosition.DistanceTo(desired);
        GlobalPosition = error > 3f
            ? desired
            : GlobalPosition.Lerp(desired, 1f - Mathf.Exp((float) (-20d * delta)));
    }

    private void Reconcile(EntitySnapshot snapshot)
    {
        _predictionHistory.RemoveAll(frame =>
            !MovementSimulation.IsSequenceNewer(
                frame.Command.Sequence,
                snapshot.LastProcessedSequence));

        var replayed = snapshot.Position;
        foreach (var frame in _predictionHistory)
        {
            replayed = MovementSimulation.Step(
                replayed,
                frame.Command.Target,
                _settings,
                _fixedDelta);
        }

        _predictedPosition = replayed;
    }

    private void UpdateRemote()
    {
        if (_remotePoints.Count == 0)
            return;

        var renderTime = NowSeconds() - RemoteInterpolationDelaySeconds;
        while (_remotePoints.Count >= 2 && _remotePoints[1].ReceivedAt <= renderTime)
            _remotePoints.RemoveAt(0);

        NumericsVector2 position;
        if (_remotePoints.Count >= 2)
        {
            var from = _remotePoints[0];
            var to = _remotePoints[1];
            var duration = Math.Max(0.0001, to.ReceivedAt - from.ReceivedAt);
            var amount = (float) Math.Clamp((renderTime - from.ReceivedAt) / duration, 0d, 1d);
            position = NumericsVector2.Lerp(from.Position, to.Position, amount);
        }
        else
        {
            position = _remotePoints[0].Position;
        }

        GlobalPosition = ToGodot(position);
    }

    private static Vector3 ToGodot(NumericsVector2 position) =>
        new(position.X, 1f, position.Y);

    private static double NowSeconds() => Time.GetTicksMsec() / 1000d;

    private readonly record struct PredictedFrame(MoveCommand Command);

    private readonly record struct RemotePoint(
        double ReceivedAt,
        NumericsVector2 Position);
}
