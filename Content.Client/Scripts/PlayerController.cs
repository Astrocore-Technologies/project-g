using Content.Shared.Movement;
using Content.Shared.Network;
using Content.Shared.Navigation;
using Godot;
using ProjectG.Networking;
using NumericsVector2 = System.Numerics.Vector2;

namespace ProjectG.Gameplay;

public partial class PlayerController : CharacterBody3D
{
	// NC: the editable appearance is shared by world avatars and the character-window preview.
	[Export] public PackedScene AppearanceScene { get; set; } = null!;
	// Presentation asset for this character; independent of the prototype world mesh.
	[Export] public Texture2D? HudPortrait { get; set; }
	[Export] public Texture2D? CharacterPortrait { get; set; }
	[Export(PropertyHint.Range, "0.05,0.5,0.01")] public float CursorHoldThresholdSeconds { get; set; } = .18f;
	private Node3D _appearance = null!;
	// NC: the visual rig is separate from the movement/collision authority.
	public ProjectG.Actors.ActorVisual Animator => (ProjectG.Actors.ActorVisual)_appearance;
	private const double RemoteInterpolationDelaySeconds = 0.1;
	private const int MaxPredictionHistory = 128;
	private const int MaxRemoteSnapshots = 20;
	internal const float HardCorrectionDistance = .5f;

	private readonly List<PredictedFrame> _predictionHistory = new();
	internal int PredictionFrameCount => _predictionHistory.Count;
	private readonly List<RemotePoint> _remotePoints = new();

	private NetworkClient? _network;
	private MovementSettings _settings;
	private NavigationGrid _navigation = null!;
	private NavigationMover? _motion;
	private NumericsVector2 _predictedPosition;
	private NumericsVector2 _target;
	private float _predictedHeight, _targetHeight, _airOffset;
	private Node3D _visual = null!;
	private ProjectG.Camera.GameplayCamera _cameraRig = null!;
	public CombatControlPhase Control { get; private set; }
	public float AirOffset => _airOffset;
	private double _controlUntil, _recoveryUntil;
	public double ControlRemaining => Math.Max(0,_controlUntil-NowSeconds());
	public double RecoveryRemaining => Math.Max(0,_recoveryUntil-NowSeconds());
	public void SetRecoveryRemaining(double seconds) => _recoveryUntil=NowSeconds()+seconds;
	public System.Numerics.Vector3 PredictedFoot => new(_predictedPosition.X, _predictedHeight + _airOffset, _predictedPosition.Y);
	internal NavigationGrid Navigation => _navigation;
	private double _tickAccumulator;
	private float _fixedDelta;
	private uint _sequence;
	private MovementCommandKind? _pendingMoveKind;
	private MovementCommandKind _navigationKind = MovementCommandKind.Destination;
	private bool _hardCorrection;
	private uint _navigationOrder;
	private uint _cancelledNavigationOrder;
	private uint _clientTick;
	private uint _lastServerTick;
	private bool _isLocal;
	private EntitySnapshot _authoritative;
	private DashPrediction? _dash;
	private bool _rightHeld;
	private double _cursorRefresh;
	private double _rightPressedAt;
	private bool _tapTargetValid;
	private NumericsVector2 _tapTarget;
	private float _tapHeight;
	private float _defenseMovement = 1;
	public bool DefenseHeld { get; set; }
	public event Action? ManualMoveRequested;
	public bool IsDashing => _dash is not null || _motion?.IsDashing == true;
	public void SetDefenseMovement(float multiplier)
	{
		_defenseMovement=multiplier;
		if (multiplier <= 0) _rightHeld=false;
	}
	public bool MoveTo(NumericsVector2 target, float? height = null)
		=> SetMoveTarget(target, height, newCommand: true);
	private bool SetMoveTarget(NumericsVector2 target, float? height, bool newCommand)
	{
		if (!_isLocal || !IsAlive || _motion is null || Control != CombatControlPhase.None || _defenseMovement <= 0 || IsDashing) return false;
		target=MovementSimulation.ClampTarget(target,_settings);
		if (!_motion.TrySetTarget(target, height ?? _motion.Height)) return false;
		if (newCommand)
		{
			_navigationOrder = _network!.NextInputOrder();
			_pendingMoveKind = MovementCommandKind.Destination;
		}
		else _pendingMoveKind = MovementCommandKind.Destination;
		_navigationKind = MovementCommandKind.Destination;
		_target=_motion.Target; _targetHeight=_motion.TargetHeight; return true;
	}
	internal bool MoveCursorTo(NumericsVector2 target, bool begin = false)
	{
		if (!_isLocal || !IsAlive || _motion is null || Control != CombatControlPhase.None || _defenseMovement <= 0 || IsDashing) return false;
		target = MovementSimulation.ClampTarget(target, _settings);
		if (!_motion.TrySetCursor(target)) return false;
		if (begin) _navigationOrder = _network!.NextInputOrder();
		_navigationKind = MovementCommandKind.Cursor;
		_pendingMoveKind = begin ? MovementCommandKind.CursorStart : MovementCommandKind.Cursor;
		_target = target; _targetHeight = _motion.Height;
		return true;
	}
	public void StopMovement()
	{
		if (MoveTo(_predictedPosition)) { _rightHeld = false; _pendingMoveKind = _navigationKind = MovementCommandKind.Stop; }
	}
	internal void ReleaseCursor(bool tap)
	{
		_rightHeld = false;
		if (NavigationCancelled(_navigationOrder)) return;
		if (!tap || !_tapTargetValid || !SetMoveTarget(_tapTarget, _tapHeight, newCommand: false)) StopMovement();
	}
	public bool CanReachDirectly(NumericsVector2 point) => _navigation.TraverseSurface(PredictedFoot, new(point.X, _predictedHeight, point.Y));
	public bool CanAttack(System.Numerics.Vector3 point, float range) => System.Numerics.Vector3.DistanceSquared(PredictedFoot, point) <= range * range && _navigation.ClearAttack(PredictedFoot, point);

	public NetworkEntityId EntityId { get; private set; } = NetworkEntityId.Invalid;
	public uint ClientTick => _clientTick;
	public NumericsVector2 PredictedPosition => _predictedPosition;
	public bool IsAlive { get; private set; } = true;
	public ProjectG.Combat.SwordAttackAnimation SwordAnimation { get; private set; } = null!;
	public override void _Ready()
	{
		SwordAnimation = GetNode<ProjectG.Combat.SwordAttackAnimation>("SwordAttackRig");
		_cameraRig = GetNode<ProjectG.Camera.GameplayCamera>("CameraRig");
		if (AppearanceScene is null)
			throw new InvalidOperationException("PlayerAvatar requires an appearance scene.");
		// NC: the appearance starts at the feet; its pose never moves the collision or camera.
		_appearance = AppearanceScene.Instantiate<Node3D>();
		_appearance.Name = "Appearance";
		_appearance.Position = new Vector3(0, -1, 0);
		_visual=GetNode<Node3D>("Visual"); _visual.AddChild(_appearance);
		SwordAnimation.Bind(Animator);
	}
	public void SetAlive(bool alive)
	{
		IsAlive = alive;
		Animator.SetAlive(alive);
		if (alive) return;
		Control=CombatControlPhase.None; _airOffset=0;
		SwordAnimation.Stop();
		_dash = null; _predictionHistory.Clear();
		_rightHeld=false; DefenseHeld=false;
		_pendingMoveKind = null;
		_target = _predictedPosition = _authoritative.Position;
		_motion?.Reset(_target, _target, _authoritative.Height, _authoritative.Height);
	}

	public void Initialize(
		PlayerSpawn spawn,
		bool isLocal,
		NetworkClient network)
	{
		EntityId = spawn.EntityId;
		_isLocal = isLocal;
		_network = network;
		if (isLocal) _navigationOrder = network.NextInputOrder();
		_settings = spawn.Movement;
		_navigation = network.Navigation ?? throw new InvalidOperationException("Navigation must arrive before spawn.");
		if (isLocal)
			_motion = new NavigationMover(_navigation, _settings,
				new NavigationPathfinder(_navigation), spawn.Position, spawn.Height);
		_fixedDelta = 1f / network.ServerTickRate;
		_predictedPosition = spawn.Position;
		_target = spawn.Position; _targetHeight = _predictedHeight = spawn.Height;
		_lastServerTick = spawn.ServerTick;
		_authoritative = new(spawn.EntityId, spawn.Position, 0, spawn.Position, Height: spawn.Height, TargetHeight: spawn.Height);
		GlobalPosition = ToGodot(spawn.Position, spawn.Height);

		var camera = GetNode<Camera3D>("CameraRig/Camera3D");
		camera.Current = isLocal;
		_cameraRig.Initialize(isLocal);

		var collision = GetNode<CollisionShape3D>("CollisionShape3D");
		var height = 2f * MathF.Max(1f, _navigation.AgentRadius);
		collision.Shape = new CapsuleShape3D { Radius = _navigation.AgentRadius, Height = height };
		collision.Disabled = !isLocal;

		// NC: preserve the model's authored scale/materials; only offset its floor origin.
		_appearance.Position = new Vector3(0, -height / 2, 0);

		if (!isLocal)
			_remotePoints.Add(new RemotePoint(NowSeconds(), spawn.Position, spawn.Height));
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

		_tapTargetValid = TryCursorSurface(out _tapTarget, out _tapHeight, mouseEvent.Position);
		if (TryCursorDirection(out var cursorPoint, mouseEvent.Position) && MoveCursorTo(cursorPoint, begin: true))
		{ _rightHeld=true; _rightPressedAt=NowSeconds(); _cursorRefresh=0; ManualMoveRequested?.Invoke(); }

	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isLocal && _rightHeld)
		{
			if (!Input.IsMouseButtonPressed(MouseButton.Right) || !GetWindow().HasFocus() || ProjectG.UI.GameUi.GameplayModalOpen)
			{
				var tap = GetWindow().HasFocus() && !ProjectG.UI.GameUi.GameplayModalOpen &&
					NowSeconds() - _rightPressedAt < CursorHoldThresholdSeconds;
				ReleaseCursor(tap);
			}
			else if ((_cursorRefresh+=delta)>=1d / NetworkConstants.MovementInputRate && GetViewport().GuiGetHoveredControl() is null)
			{ _cursorRefresh %= 1d / NetworkConstants.MovementInputRate; if (TryCursorDirection(out var point)) MoveCursorTo(point); }
		}
		if (_isLocal)
			UpdateLocal(delta);
	}

	public override void _Process(double delta)
	{
		if (!_isLocal)
            UpdateRemote();
        // NC: snapshot control tilts only the capsule appearance, never the collision or camera root.
        Animator.SetControl(Control, ControlRemaining, IsDashing);
		var running = _isLocal ? _motion?.IsCursor == true && _motion.IsMoving && _defenseMovement > 0 && Control == CombatControlPhase.None
			: _authoritative.RunningIntent;
		Animator.SetMovementIntent(_isLocal ? _target-_predictedPosition : _authoritative.Target-_authoritative.Position,
			IsAlive && running ? _settings.Speed * (_isLocal ? _defenseMovement : 1) : 0);
	}

	public void ApplySnapshot(EntitySnapshot snapshot, uint serverTick)
	{
		// Chunks may arrive out of order, including packets from an earlier AOI visit.
		if (!MovementSimulation.IsSequenceNewer(serverTick, _lastServerTick))
			return;
		_lastServerTick = serverTick;
        Control=snapshot.Control; _controlUntil=NowSeconds()+snapshot.ControlRemaining;
        _airOffset=snapshot.AirOffset;
        if (Control != CombatControlPhase.None) { _rightHeld=false; _dash=null; }
        if (_isLocal)
		{
			ApplyNavigationCancellation(snapshot);
			if (!IsAlive)
			{
				_authoritative = snapshot; _predictedPosition = snapshot.Position;
				_predictedHeight = snapshot.Height; GlobalPosition = ToGodot(snapshot.Position, snapshot.Height);
				return;
			}
			Reconcile(snapshot);
			return;
		}

		_authoritative = snapshot;
		_remotePoints.Add(new RemotePoint(NowSeconds(), snapshot.Position, snapshot.Height, snapshot.AirOffset));
		if (_remotePoints.Count > MaxRemoteSnapshots)
			_remotePoints.RemoveAt(0);
	}

	private void UpdateLocal(double delta)
	{
		if (_motion is null || !IsAlive || _network?.RegionActive == false)
			return;
		if (_dash is { } expired && NowSeconds() - expired.StartedAt > 2)
			RejectDash(expired.Sequence);
		_tickAccumulator += delta;
		var simulatedTicks = 0;

		while (_tickAccumulator >= _fixedDelta && simulatedTicks++ < 4)
		{
			_tickAccumulator -= _fixedDelta;
			_clientTick++;
			var sendInput = _pendingMoveKind is not null;
			if (sendInput && ++_sequence == 0) ++_sequence;

			// A predicted dash endpoint must never become an ordinary server movement intention.
			var commandTarget = _dash is { } dash && _target == dash.Destination ? dash.PreviousTarget : _target;
			var command = new MoveCommand(_sequence, _clientTick, commandTarget, _dash is { } pendingHeight && commandTarget == pendingHeight.PreviousTarget ? pendingHeight.PreviousHeight : _targetHeight, _navigation.SurfaceHash, _navigationOrder,
				_pendingMoveKind ?? _navigationKind);
			if (_dash is { Applied: false } pending && _clientTick >= pending.ClientTick)
			{
				StartPredictedDash(pending);
				_dash = pending with { Applied = true };
			}
			var cancelled = NavigationCancelled(command.InputOrder);
			if (!cancelled) ApplyMovementFrame(command);
			_motion.Step(_fixedDelta*(_motion.IsDashing ? 1 : Control != CombatControlPhase.None ? 0 : _defenseMovement));
			_predictedPosition = _motion.Position; _predictedHeight = _motion.Height;

			if (!cancelled && (_sequence != 0 || _dash is not null)) _predictionHistory.Add(new PredictedFrame(command,_defenseMovement));
			if (_predictionHistory.Count > MaxPredictionHistory)
				_predictionHistory.RemoveAt(0);

			if (!cancelled && sendInput) _network?.SendMove(command);
			_pendingMoveKind = null;
		}

		var desired = ToGodot(_predictedPosition, _predictedHeight + _airOffset);
		var error = GlobalPosition.DistanceTo(desired);
		// A correction across a corner must not visually ease through solid geometry.
		var current = new NumericsVector2(GlobalPosition.X, GlobalPosition.Z);
		GlobalPosition = _hardCorrection || error > HardCorrectionDistance || !_navigation.TraverseSurface(new(current.X, GlobalPosition.Y - 1 - _airOffset, current.Y), new(_predictedPosition.X,_predictedHeight,_predictedPosition.Y))
			? desired
			: GlobalPosition.Lerp(desired, 1f - Mathf.Exp((float) (-20d * delta)));
		_hardCorrection = false;
	}

	private void Reconcile(EntitySnapshot snapshot)
	{
		_authoritative = snapshot;
        if (snapshot.Control != CombatControlPhase.None) { _target=snapshot.Target; _targetHeight=snapshot.TargetHeight; }
		if (_dash is { } pending && !MovementSimulation.IsSequenceNewer(pending.Sequence, snapshot.LastAbilitySequence))
		{
			if (_target == pending.Destination) { _target = snapshot.Target; _targetHeight = snapshot.TargetHeight; }
			_dash = null;
		}
		var before = PredictedFoot;
		Replay(snapshot);
		if (System.Numerics.Vector3.Distance(before, PredictedFoot) > HardCorrectionDistance) _hardCorrection = true;
		// Once the server acknowledges a rejected/adjusted target, it owns the next predicted route too.
		if (!MovementSimulation.IsSequenceNewer(_sequence, snapshot.LastProcessedSequence) && _pendingMoveKind is null && _dash is null)
		{ _target = snapshot.Target; _targetHeight = snapshot.TargetHeight; _navigationKind = snapshot.DirectCursor ? MovementCommandKind.Cursor : MovementCommandKind.Destination; }
	}

	private void Replay(EntitySnapshot snapshot)
	{
		if (_motion is null || !_motion.Restore(snapshot.Position, snapshot.Target, snapshot.DashDestination, snapshot.DashSpeed, snapshot.Height, snapshot.TargetHeight, snapshot.DashHeight, snapshot.DirectCursor))
			return;
		_predictionHistory.RemoveAll(frame => NavigationCancelled(frame.Command.InputOrder) ||
			MovementSimulation.IsSequenceNewer(snapshot.LastProcessedSequence, frame.Command.Sequence) ||
			frame.Command.Sequence == snapshot.LastProcessedSequence &&
			!MovementSimulation.IsSequenceNewer(frame.Command.ClientTick, snapshot.ProcessedClientTick));

		var injected = false;
		foreach (var frame in _predictionHistory)
		{
			// Replay an unacknowledged dash once, at its original client tick, never once per frame.
			if (!injected && _dash is { } pending && frame.Command.ClientTick >= pending.ClientTick)
			{
				StartPredictedDash(pending);
				injected = true;
			}
			// An acknowledged command continues from the server's target (not the old predicted Stop point).
			var replayCommand = frame.Command.Sequence == snapshot.LastProcessedSequence
				? frame.Command with { Target = snapshot.Target, TargetHeight = snapshot.TargetHeight,
					Kind = snapshot.DirectCursor ? MovementCommandKind.Cursor : MovementCommandKind.Destination }
				: frame.Command;
			ApplyMovementFrame(replayCommand);
			_motion.Step(_fixedDelta*(_motion.IsDashing ? 1 : Control != CombatControlPhase.None ? 0 : frame.SpeedMultiplier));
		}

		_predictedPosition = _motion.Position; _predictedHeight = _motion.Height;
	}

	public bool PredictDash(uint sequence, NumericsVector2 direction, float range, float speed)
	{
		if (!_isLocal || !IsAlive || _motion is null || _dash is not null || _motion.IsDashing || Control != CombatControlPhase.None ||
			!SurfaceDash.TryDestination(_navigation, PredictedFoot, direction, range, out var destination, out var height)) return false;
		_dash = new(sequence, _clientTick + 1, direction, range, speed, destination, _target, NowSeconds(), false, height, _targetHeight);
		_target = destination; _targetHeight = height;
		return true;
	}

	public void RejectDash(uint sequence)
	{
		if (_dash is not { } pending || pending.Sequence != sequence) return;
		if (_target == pending.Destination) { _target = pending.PreviousTarget; _targetHeight = pending.PreviousHeight; }
		_dash = null;
		Replay(_authoritative);
	}

	private void StartPredictedDash(DashPrediction prediction)
	{
		if (_motion is not null && SurfaceDash.TryDestination(_navigation, _motion.Foot,
			prediction.Direction, prediction.Range, out var destination, out var height))
			_motion.TryStartDash(destination, prediction.Speed, height);
	}

	private void ApplyMovementFrame(MoveCommand command)
	{
		// Hold at the predicted endpoint until authority acknowledges it; a new RMB still queues normally.
		if (_dash is { } dash && command.ClientTick >= dash.ClientTick && command.Target == dash.PreviousTarget &&
			_target == dash.Destination) return;
		if (Control == CombatControlPhase.None)
		{
			if (command.Kind is MovementCommandKind.Cursor or MovementCommandKind.CursorStart) _motion?.TrySetCursor(command.Target);
			else _motion?.TrySetTarget(command.Target, command.TargetHeight);
		}
	}

	private void UpdateRemote()
	{
		if (_remotePoints.Count == 0)
			return;

		var renderTime = NowSeconds() - RemoteInterpolationDelaySeconds;
		while (_remotePoints.Count >= 2 && _remotePoints[1].ReceivedAt <= renderTime)
			_remotePoints.RemoveAt(0);

		NumericsVector2 position; float height, air;
		if (_remotePoints.Count >= 2)
		{
			var from = _remotePoints[0];
			var to = _remotePoints[1];
			var duration = Math.Max(0.0001, to.ReceivedAt - from.ReceivedAt);
			var amount = (float) Math.Clamp((renderTime - from.ReceivedAt) / duration, 0d, 1d);
			position = NumericsVector2.Lerp(from.Position, to.Position, amount); height = float.Lerp(from.Height, to.Height, amount); air = float.Lerp(from.Air,to.Air,amount);
			if (!_navigation.TraverseSurface(new(from.Position.X, from.Height, from.Position.Y), new(to.Position.X, to.Height, to.Position.Y)))
				{ position = from.Position; height = from.Height; }
		}
		else
		{
			position = _remotePoints[0].Position; height = _remotePoints[0].Height; air = _remotePoints[0].Air;
		}

		GlobalPosition = ToGodot(position, height + air);
	}

	private Vector3 ToGodot(NumericsVector2 position, float height = 0) =>
		new(position.X, height + MathF.Max(1f, _navigation.AgentRadius), position.Y);

	private static double NowSeconds() => Time.GetTicksMsec() / 1000d;

	private bool NavigationCancelled(uint order) => _cancelledNavigationOrder != 0 &&
		!MovementSimulation.IsSequenceNewer(order, _cancelledNavigationOrder);

	private void ApplyNavigationCancellation(EntitySnapshot snapshot)
	{
		var cancelled = snapshot.CancelledNavigationOrder;
		if (cancelled == 0 || _cancelledNavigationOrder != 0 &&
			!MovementSimulation.IsSequenceNewer(cancelled, _cancelledNavigationOrder)) return;
		_cancelledNavigationOrder = cancelled;
		if (!NavigationCancelled(_navigationOrder)) return;
		_rightHeld = false;
		_pendingMoveKind = null;
		_target = snapshot.Target;
		_targetHeight = snapshot.TargetHeight;
	}

	private readonly record struct PredictedFrame(MoveCommand Command,float SpeedMultiplier);
	public bool TryCursorGround(out NumericsVector2 point)
	{
		return TryCursorSurface(out point,out _);
	}
	private readonly record struct DashPrediction(uint Sequence, uint ClientTick, NumericsVector2 Direction,
		float Range, float Speed, NumericsVector2 Destination, NumericsVector2 PreviousTarget, double StartedAt, bool Applied, float DestinationHeight, float PreviousHeight);

	private readonly record struct RemotePoint(
		double ReceivedAt,
		NumericsVector2 Position, float Height, float Air = 0);
}
