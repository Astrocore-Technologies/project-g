using Content.Shared.Movement;
using Content.Shared.Network;
using Content.Shared.Navigation;
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
    private NavigationGrid _navigation = null!;
    private NavigationMover? _motion;
    private NumericsVector2 _predictedPosition;
    private NumericsVector2 _target;
    private double _tickAccumulator;
    private float _fixedDelta;
    private uint _sequence;
    private uint _clientTick;
    private uint _lastServerTick;
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
        _navigation = network.Navigation ?? throw new InvalidOperationException("Navigation must arrive before spawn.");
        if (isLocal)
            _motion = new NavigationMover(_navigation, _settings,
                new NavigationPathfinder(_navigation), spawn.Position);
        _fixedDelta = 1f / network.ServerTickRate;
        _predictedPosition = spawn.Position;
        _target = spawn.Position;
        _lastServerTick = spawn.ServerTick;
        GlobalPosition = ToGodot(spawn.Position);

        var camera = GetNode<Camera3D>("CameraRig/Camera3D");
        camera.Current = isLocal;

        var collision = GetNode<CollisionShape3D>("CollisionShape3D");
        var height = 2f * MathF.Max(1f, _navigation.AgentRadius);
        collision.Shape = new CapsuleShape3D { Radius = _navigation.AgentRadius, Height = height };
        collision.Disabled = !isLocal;

        var material = new StandardMaterial3D
        {
            AlbedoColor = isLocal
                ? new Color(0.2f, 0.8f, 0.35f)
                : new Color(0.9f, 0.45f, 0.2f)
        };
        var mesh = GetNode<MeshInstance3D>("MeshInstance3D");
        mesh.Mesh = new CapsuleMesh { Radius = _navigation.AgentRadius, Height = height };
        mesh.MaterialOverride = material;

        if (!isLocal)
            _remotePoints.Add(new RemotePoint(NowSeconds(), spawn.Position));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isLocal ||
            @event is not InputEventMouseButton mouseEvent ||
            mouseEvent.ButtonIndex != MouseButton.Right ||
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
        var target = MovementSimulation.ClampTarget(
            new NumericsVector2(clicked.X, clicked.Z),
            _settings);
        if (_motion?.TrySetTarget(target) == true)
            _target = target;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isLocal)
            UpdateLocal(delta);
    }

    public override void _Process(double delta)
    {
        if (!_isLocal)
            UpdateRemote();
    }

    public void ApplySnapshot(EntitySnapshot snapshot, uint serverTick)
    {
        // Chunks may arrive out of order, including packets from an earlier AOI visit.
        if (!MovementSimulation.IsSequenceNewer(serverTick, _lastServerTick))
            return;
        _lastServerTick = serverTick;
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
        if (_motion is null)
            return;
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
            _motion.TrySetTarget(command.Target);
            _motion.Step(_fixedDelta);
            _predictedPosition = _motion.Position;

            _predictionHistory.Add(new PredictedFrame(command));
            if (_predictionHistory.Count > MaxPredictionHistory)
                _predictionHistory.RemoveAt(0);

            _network?.SendMove(command);
        }

        var desired = ToGodot(_predictedPosition);
        var error = GlobalPosition.DistanceTo(desired);
        // A correction across a corner must not visually ease through solid geometry.
        var current = new NumericsVector2(GlobalPosition.X, GlobalPosition.Z);
        GlobalPosition = error > 3f || !_navigation.CanTraverse(current, _predictedPosition)
            ? desired
            : GlobalPosition.Lerp(desired, 1f - Mathf.Exp((float) (-20d * delta)));
    }

    private void Reconcile(EntitySnapshot snapshot)
    {
        if (_motion is null || !_motion.Reset(snapshot.Position, snapshot.Target))
            return;
        _predictionHistory.RemoveAll(frame =>
            !MovementSimulation.IsSequenceNewer(
                frame.Command.Sequence,
                snapshot.LastProcessedSequence));

        foreach (var frame in _predictionHistory)
        {
            _motion.TrySetTarget(frame.Command.Target);
            _motion.Step(_fixedDelta);
        }

        _predictedPosition = _motion.Position;
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
            if (!_navigation.CanTraverse(from.Position, to.Position))
                position = from.Position;
        }
        else
        {
            position = _remotePoints[0].Position;
        }

        GlobalPosition = ToGodot(position);
    }

    private Vector3 ToGodot(NumericsVector2 position) =>
        new(position.X, MathF.Max(1f, _navigation.AgentRadius), position.Y);

    private static double NowSeconds() => Time.GetTicksMsec() / 1000d;

    private readonly record struct PredictedFrame(MoveCommand Command);

    private readonly record struct RemotePoint(
        double ReceivedAt,
        NumericsVector2 Position);
}
