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
	private EntitySnapshot _authoritative;
	private DashPrediction? _dash;
	private bool _rightHeld;
	private double _cursorRefresh;
	private float _defenseMovement = 1;
	public bool DefenseHeld { get; set; }
	public event Action? ManualMoveRequested;
	public bool IsDashing => _dash is not null || _motion?.IsDashing == true;
	public void SetDefenseMovement(float multiplier) => _defenseMovement=multiplier;
	public bool MoveTo(NumericsVector2 target)
	{
		if (!_isLocal || !IsAlive || _motion is null) return false;
		target=MovementSimulation.ClampTarget(target,_settings);
		if (!_motion.TrySetTarget(target)) return false;
		_target=target; return true;
	}
	public void StopMovement() { if (!IsDashing) MoveTo(_predictedPosition); }
	public bool CanReachDirectly(NumericsVector2 point) => _navigation.CanTraverse(_predictedPosition,point);

	public NetworkEntityId EntityId { get; private set; } = NetworkEntityId.Invalid;
	public uint ClientTick => _clientTick;
	public NumericsVector2 PredictedPosition => _predictedPosition;
	public bool IsAlive { get; private set; } = true;
	public void SetAlive(bool alive)
	{
		IsAlive = alive;
		if (alive) return;
		_dash = null; _predictionHistory.Clear();
		_rightHeld=false; DefenseHeld=false;
		_target = _predictedPosition = _authoritative.Position;
		_motion?.Reset(_target, _target);
	}

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
		_authoritative = new(spawn.EntityId, spawn.Position, 0, spawn.Position);
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
		if (ProjectG.UI.GameUi.GameplayModalOpen) return;
		if (!_isLocal || !IsAlive ||
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
		_rightHeld=true; ManualMoveRequested?.Invoke();
		var target = MovementSimulation.ClampTarget(
			new NumericsVector2(clicked.X, clicked.Z),
			_settings);
		if (_motion?.TrySetTarget(target) == true)
			_target = target;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isLocal && _rightHeld)
		{
			if (!Input.IsMouseButtonPressed(MouseButton.Right) || !GetWindow().HasFocus() || ProjectG.UI.GameUi.GameplayModalOpen) _rightHeld=false;
			else if ((_cursorRefresh+=delta)>=.1 && GetViewport().GuiGetHoveredControl() is null)
			{ _cursorRefresh=0; if (TryCursorGround(out var point)) MoveTo(point); }
		}
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
			if (!IsAlive)
			{
				_authoritative = snapshot; _predictedPosition = snapshot.Position;
				GlobalPosition = ToGodot(snapshot.Position);
				return;
			}
			Reconcile(snapshot);
			return;
		}

		_remotePoints.Add(new RemotePoint(NowSeconds(), snapshot.Position));
		if (_remotePoints.Count > MaxRemoteSnapshots)
			_remotePoints.RemoveAt(0);
	}

	private void UpdateLocal(double delta)
	{
		if (_motion is null || !IsAlive)
			return;
		if (_dash is { } expired && NowSeconds() - expired.StartedAt > 2)
			RejectDash(expired.Sequence);
		_tickAccumulator += delta;
		var simulatedTicks = 0;

		while (_tickAccumulator >= _fixedDelta && simulatedTicks++ < 4)
		{
			_tickAccumulator -= _fixedDelta;
			_clientTick++;
			_sequence++;
			if (_sequence == 0)
				_sequence++;

			// A predicted dash endpoint must never become an ordinary server movement intention.
			var commandTarget = _dash is { } dash && _target == dash.Destination ? dash.PreviousTarget : _target;
			var command = new MoveCommand(_sequence, _clientTick, commandTarget);
			if (_dash is { Applied: false } pending && _clientTick >= pending.ClientTick)
			{
				StartPredictedDash(pending);
				_dash = pending with { Applied = true };
			}
			ApplyMovementFrame(command);
			_motion.Step(_fixedDelta*(_motion.IsDashing ? 1 : _defenseMovement));
			_predictedPosition = _motion.Position;

			_predictionHistory.Add(new PredictedFrame(command,_defenseMovement));
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
		_authoritative = snapshot;
		if (_dash is { } pending && !MovementSimulation.IsSequenceNewer(pending.Sequence, snapshot.LastAbilitySequence))
		{
			if (_target == pending.Destination) _target = snapshot.Target;
			_dash = null;
		}
		Replay(snapshot);
	}

	private void Replay(EntitySnapshot snapshot)
	{
		if (_motion is null || !_motion.Restore(snapshot.Position, snapshot.Target, snapshot.DashDestination, snapshot.DashSpeed))
			return;
		_predictionHistory.RemoveAll(frame =>
			!MovementSimulation.IsSequenceNewer(
				frame.Command.Sequence,
				snapshot.LastProcessedSequence));

		var injected = false;
		foreach (var frame in _predictionHistory)
		{
			// Replay an unacknowledged dash once, at its original client tick, never once per frame.
			if (!injected && _dash is { } pending && frame.Command.ClientTick >= pending.ClientTick)
			{
				StartPredictedDash(pending);
				injected = true;
			}
			ApplyMovementFrame(frame.Command);
			_motion.Step(_fixedDelta*(_motion.IsDashing ? 1 : frame.SpeedMultiplier));
		}

		_predictedPosition = _motion.Position;
	}

	public bool PredictDash(uint sequence, NumericsVector2 direction, float range, float speed)
	{
		if (!_isLocal || !IsAlive || _motion is null || _dash is not null || _motion.IsDashing ||
			!DashGeometry.TryDestination(_navigation, _predictedPosition, direction, range, out var destination)) return false;
		_dash = new(sequence, _clientTick + 1, direction, range, speed, destination, _target, NowSeconds(), false);
		_target = destination;
		return true;
	}

	public void RejectDash(uint sequence)
	{
		if (_dash is not { } pending || pending.Sequence != sequence) return;
		if (_target == pending.Destination) _target = pending.PreviousTarget;
		_dash = null;
		Replay(_authoritative);
	}

	private void StartPredictedDash(DashPrediction prediction)
	{
		if (_motion is not null && DashGeometry.TryDestination(_navigation, _motion.Position,
			prediction.Direction, prediction.Range, out var destination))
			_motion.TryStartDash(destination, prediction.Speed);
	}

	private void ApplyMovementFrame(MoveCommand command)
	{
		// Hold at the predicted endpoint until authority acknowledges it; a new RMB still queues normally.
		if (_dash is { } dash && command.ClientTick >= dash.ClientTick && command.Target == dash.PreviousTarget &&
			_target == dash.Destination) return;
		_motion?.TrySetTarget(command.Target);
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

	private readonly record struct PredictedFrame(MoveCommand Command,float SpeedMultiplier);
	public bool TryCursorGround(out NumericsVector2 point)
	{
		point=default; var camera=GetViewport().GetCamera3D(); if(camera is null) return false;
		var mouse=GetViewport().GetMousePosition(); var origin=camera.ProjectRayOrigin(mouse); var ray=camera.ProjectRayNormal(mouse);
		if(Mathf.Abs(ray.Y)<.0001f || -origin.Y/ray.Y<=0) return false;
		var hit=origin+ray*(-origin.Y/ray.Y); point=new(hit.X,hit.Z); return true;
	}
	private readonly record struct DashPrediction(uint Sequence, uint ClientTick, NumericsVector2 Direction,
		float Range, float Speed, NumericsVector2 Destination, NumericsVector2 PreviousTarget, double StartedAt, bool Applied);

	private readonly record struct RemotePoint(
		double ReceivedAt,
		NumericsVector2 Position);
}
