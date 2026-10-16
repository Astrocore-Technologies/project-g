using Content.Shared.Movement;
using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.Gameplay;
using NumericsVector2 = System.Numerics.Vector2;

namespace ProjectG.Combat;

/// <summary>Predicts a swing only. HP, hits, critical rolls and damage are never predicted here.</summary>
public partial class CombatPresentation : Node3D
{
    private readonly Label3D _healthLabel = new() { Position = new Vector3(0, 1.5f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
    private readonly MeshInstance3D _slash = new() { Visible = false };
    private readonly MeshInstance3D _areaVisual = new() { Visible = false };
    private double _areaRemaining;
    private uint _areaSequence;
    private readonly StandardMaterial3D _slashMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled
    };
    private NetworkClient? _network;
    private Node3D _actor = null!;
    private CombatState _state;
    private bool _local;
    private ActorLifeState? _life;
    private double _lifeReceived;
    private bool _corpseVisibilityPending;
    private PlayerController? _player;
    private SwordAttackAnimation? _swordAnimation;
    private bool _continuousHeld;
    private double _heartbeatAt;
    private NumericsVector2 _basicDirection = NumericsVector2.UnitY;
    private ulong _lastAttackExecution;
    private ulong _lastFeedbackId;
    private uint _sequence;
    private uint _pending;
    private uint _lastAttackSequence;
    private uint _spawnTick;
    private double _nextAttackAt;
    private double _pendingSince;
    private double _slashRemaining;
    private double _damageRemaining;
    private string _damageText = "";
    public bool IsAlive => _state.Health > 0;
    public double Health => _state.Health;
    public double MaxHealth => _state.MaxHealth;

    public void Initialize(CombatState state, NetworkClient network, bool local)
    {
        _actor = GetParent<Node3D>();
        _network = network;
        _local = local;
        _player=local ? _actor as PlayerController : null;
        _swordAnimation=(_actor as PlayerController)?.SwordAnimation;
        if (_player is not null) _player.ManualMoveRequested+=CancelBasicAttack;
        _spawnTick = state.ServerTick;
        AddChild(_healthLabel);
        AddChild(_slash);
        AddChild(_areaVisual);
        _slash.MaterialOverride = _slashMaterial;
        ApplyState(state);
        network.LifeReceived+=ApplyLife;
        if(network.KnownLife.TryGetValue(state.EntityId,out var life))ApplyLife(life);
    }
    public void ApplyLife(ActorLifeState life)
    {
        if(life.EntityId!=_state.EntityId)return;
        _life=life;_lifeReceived=Now();_corpseVisibilityPending=life.Dead&&life.CorpseRemaining>0;
        _actor.Visible=!life.Dead || life.CorpseRemaining>0;
        if(life.Dead)CancelBasicAttack();
    }
    private void UpdateCorpseVisibility()
    {
        if(_corpseVisibilityPending && _life is {} life && life.CorpseRemaining-(Now()-_lifeReceived)<=0)
        {_actor.Visible=false;_corpseVisibilityPending=false;}
    }

    public void ApplyState(CombatState state)
    {
        if (_state.EntityId.IsValid && state.ServerTick != _state.ServerTick &&
            !MovementSimulation.IsSequenceNewer(state.ServerTick, _state.ServerTick))
            return;
        _state = state;
        _slash.Mesh = BuildCone(state.Range, state.HalfAngleRadians);
        UpdateLabel();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_local || !IsAlive || ProjectG.UI.GameUi.GameplayModalOpen || @event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
            return;
        if (_network?.RegionActive != true || _pending != 0 || _player?.DefenseHeld == true ||
            _player?.Control != CombatControlPhase.None || !TryBasicDirection(mouse.Position, out var direction)) return;
        CancelBasicAttack();
        // A tap produces one swing, a held button requests server-timed repetition.
        // The existing Ctrl chord remains an explicit single-swing input.
        if (mouse.CtrlPressed && Now() < _nextAttackAt) return;
        _continuousHeld = !mouse.CtrlPressed;
        Swing(direction, _continuousHeld ? BasicAttackMode.BeginContinuous : BasicAttackMode.Manual);
        GetViewport().SetInputAsHandled();
    }

    public override void _Input(InputEvent @event)
    {
        // Receive release even if UI consumed the press/focus moved afterwards.
        if (_local && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) CancelBasicAttack();
    }

    private bool TryBasicDirection(Vector2? screen, out NumericsVector2 direction)
    {
        direction = _basicDirection;
        if (_player is null || !_player.TryCursorSurface(out var point, out _, screen)) return false;
        var offset = point - _player.PredictedPosition;
        if (offset.LengthSquared() < .000001f) return false;
        direction = NumericsVector2.Normalize(offset);
        return true;
    }

    private uint NextSequence() { if (++_sequence == 0) ++_sequence; return _sequence; }

    private void Swing(NumericsVector2 direction, BasicAttackMode mode)
    {
        _pending = NextSequence();
        _pendingSince = Now();
        _basicDirection = direction;
        _heartbeatAt = Now() + NetworkConstants.ContinuousAttackHeartbeatSeconds;
        if (Now() >= _nextAttackAt)
        {
            _nextAttackAt = Now() + _state.AttackInterval;
            _swordAnimation?.PredictBasic(_sequence, direction, _state.AttackInterval);
            ShowSwing(new(_actor.GlobalPosition.X, _actor.GlobalPosition.Z), direction, _state.Range, predicted: true);
        }
        _network?.SendAttack(new(_sequence, _player?.ClientTick ?? 0, direction, mode));
    }

    public void CancelBasicAttack()
    {
        if (!_continuousHeld) return;
        _continuousHeld = false;
        _network?.SendAttack(new(NextSequence(), _player?.ClientTick ?? 0, _basicDirection, BasicAttackMode.EndContinuous));
    }

    public override void _ExitTree()
    { CancelBasicAttack(); if (_network is not null)_network.LifeReceived-=ApplyLife;if (_player is not null) _player.ManualMoveRequested -= CancelBasicAttack; }

    public override void _PhysicsProcess(double delta)
    {
        if (!_local || !_continuousHeld || _player is null) return;
        if (!Input.IsMouseButtonPressed(MouseButton.Left) || !IsAlive || !GetWindow().HasFocus() ||
            _network?.RegionActive != true || ProjectG.UI.GameUi.GameplayModalOpen || _player.DefenseHeld ||
            _player.IsDashing || _player.Control != CombatControlPhase.None || _player.RecoveryRemaining > 0)
        { CancelBasicAttack(); return; }
        if (Now() < _heartbeatAt) return;
        _heartbeatAt = Now() + NetworkConstants.ContinuousAttackHeartbeatSeconds;
        if (TryBasicDirection(null, out var direction)) _basicDirection = direction;
        _network.SendAttack(new(NextSequence(), _player.ClientTick, _basicDirection, BasicAttackMode.UpdateContinuous));
    }

    public void Confirm(AttackEvent action)
    {
        if (!IsAlive || !MovementSimulation.IsSequenceNewer(action.ServerTick, _spawnTick) ||
            (action.ExecutionId != 0 ? action.ExecutionId <= _lastAttackExecution :
                !MovementSimulation.IsSequenceNewer(action.Sequence, _lastAttackSequence)))
            return;
        _lastAttackSequence = action.Sequence;
        _lastAttackExecution = action.ExecutionId;
        if (_local) _nextAttackAt = Now() + _state.AttackInterval;
        if (action.Sequence == _areaSequence && _state.Kind == CombatEntityKind.Boss) return;
        if (_local && _pending == action.Sequence)
            _pending = 0;
        // Replace/restart the same visual with authoritative origin; do not apply damage twice.
        ShowSwing(action.Origin, action.Direction, action.Range, predicted: false);
        _swordAnimation?.ConfirmBasic(action);
        if(!action.TargetId.IsValid)
        { _damageText="ПРОМАХ"; _damageRemaining=.6; UpdateLabel(); }
    }

    public void ApplyResult(AttackResult result)
    {
        if (!_local || result.Sequence != _pending)
            return;
        _pending = 0;
        if (result.Outcome != AttackOutcome.Accepted)
        {
            CancelBasicAttack();
            _swordAnimation?.RejectBasic(result.Sequence);
            _slash.Visible = false;
            _slashRemaining = 0;
            _damageText = result.Outcome.ToString();
            _damageRemaining = 0.6;
            UpdateLabel();
        }
    }

    public void ApplyDamage(AttackEvent action)
    {
        if (action.ServerTick != _state.ServerTick &&
            !MovementSimulation.IsSequenceNewer(action.ServerTick, _state.ServerTick))
            return;
        if (action.TargetHealth > _state.MaxHealth)
            return;
        _state = _state with { Health = action.TargetHealth, ServerTick = action.ServerTick };
        _damageText = HitFeedback(action.Damage,action.Critical,action.Evaded,action.Guard);
        _damageRemaining = 0.6;
        UpdateLabel();
    }

    public void ApplyAbilityDamage(AbilityHit hit)
    {
        if (hit.ServerTick != _state.ServerTick && !MovementSimulation.IsSequenceNewer(hit.ServerTick, _state.ServerTick)) return;
        if (hit.TargetHealth > _state.MaxHealth) return;
        _state = _state with { Health = hit.TargetHealth, ServerTick = hit.ServerTick };
        _damageText = HitFeedback(hit.Damage,hit.Critical,hit.Evaded,hit.Guard); _damageRemaining = 0.6;
        UpdateLabel();
    }

    private static string HitFeedback(double damage,bool critical,bool evaded,GuardImpact guard)
    {
        if(evaded) return "УКЛОНЕНИЕ";
        if(guard==GuardImpact.Parried) return "ПАРИРОВАНИЕ";
        var defense=guard==GuardImpact.GuardBroken ? "БЛОК ПРОБИТ " : guard==GuardImpact.Blocked ? "БЛОК " : "";
        return $"{defense}{(critical ? "CRIT " : "")}-{damage:0}";
    }

    public void ApplyFeedback(CombatFeedback value)
    {
        if(value.TargetId!=_state.EntityId || value.EventId<=_lastFeedbackId ||
            value.ServerTick!=_spawnTick && !MovementSimulation.IsSequenceNewer(value.ServerTick,_spawnTick))return;
        _lastFeedbackId=value.EventId;
        _damageText=value.Kind switch
        {
            CombatFeedbackKind.Healing=>$"{(value.Critical?"CRIT ":"")}+{value.Amount:0} HP",
            CombatFeedbackKind.Resurrection=>$"ВОСКРЕШЕНИЕ +{value.Amount:0} HP",
            CombatFeedbackKind.ShieldAbsorption=>$"ЩИТ {value.Amount:0}",
            CombatFeedbackKind.LifeSteal=>$"ПОХИЩЕНИЕ +{value.Amount:0} HP",
            CombatFeedbackKind.ManaSteal=>$"ПОХИЩЕНИЕ +{value.Amount:0} MP",
            CombatFeedbackKind.Reflection=>$"ОТРАЖЕНИЕ -{value.Amount:0}",
            CombatFeedbackKind.HealthPrice=>$"ЦЕНА -{value.Amount:0} HP",
            _=>$"{(value.Critical?"CRIT ":"")}-{value.Amount:0}"
        };
        _damageRemaining=.8;UpdateLabel();
    }

    public void ShowWindup(NpcWindup value)
    {
        if (!IsAlive || value.ServerTick < _spawnTick) return;
        if (value.RemainingSeconds == 0) { _slash.Visible = false; _slashRemaining = 0; return; }
        ShowSwing(value.Origin, value.Direction, value.Range, predicted: false);
        _slashMaterial.AlbedoColor = new Color(1, 0.15f, 0.05f, 0.5f);
        _slashRemaining = value.RemainingSeconds;
    }

    public void ShowArea(NpcArea value)
    {
        if (_state.Kind != CombatEntityKind.Boss || !IsAlive ||
            (value.ServerTick != _spawnTick && !MovementSimulation.IsSequenceNewer(value.ServerTick, _spawnTick))) return;
        _areaSequence = value.Sequence;
        if (value.Phase == NpcAreaPhase.Finished) { _areaVisual.Visible = false; _areaRemaining = 0; return; }
        _areaVisual.Mesh = new CylinderMesh { TopRadius = value.Radius, BottomRadius = value.Radius, Height = 0.035f };
        _areaVisual.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = value.Phase == NpcAreaPhase.Telegraph ? new Color(1, 0.45f, 0.1f, 0.4f) : new Color(1, 0.1f, 0.05f, 0.8f)
        };
        _areaVisual.GlobalPosition = new(value.Center.X, value.Height + 0.12f, value.Center.Y);
        _areaVisual.Visible = true; _areaRemaining = value.RemainingSeconds;
    }

    public override void _Process(double delta)
    {
        UpdateCorpseVisibility();
        if (_areaRemaining > 0 && (_areaRemaining -= delta) <= 0) _areaVisual.Visible = false;
        if (_slashRemaining > 0 && (_slashRemaining -= delta) <= 0)
            _slash.Visible = false;
        if (_damageRemaining > 0 && (_damageRemaining -= delta) <= 0)
        {
            _damageText = "";
            UpdateLabel();
        }
        // Connection loss or a delayed ack cannot leave unlimited pending predictions.
        if (_pending != 0 && Now() - _pendingSince > 2)
        {
            _swordAnimation?.RejectBasic(_pending);
            _pending = 0;
            _slash.Visible = false;
        }
    }

    private void ShowSwing(NumericsVector2 origin, NumericsVector2 direction, float range, bool predicted)
    {
        _slashMaterial.AlbedoColor = predicted ? new Color(0.2f, 0.6f, 1, 0.4f) : new Color(1, 0.8f, 0.2f, 0.5f);
        _slash.GlobalPosition = new Vector3(origin.X, _actor.GlobalPosition.Y - 0.8f, origin.Y);
        _slash.Rotation = new Vector3(0, Mathf.Atan2(direction.X, direction.Y), 0);
        _slash.Visible = true;
        _slashRemaining = 0.15;
    }

    private void UpdateLabel()
    {
        if (!IsAlive) { _slash.Visible = false; _slashRemaining = 0; _areaVisual.Visible = false; _areaRemaining = 0; _swordAnimation?.Stop(); }
        _healthLabel.Text = _local ? _damageText : $"{(_state.Kind == CombatEntityKind.TrainingTarget ? "Манекен · " : _state.Kind == CombatEntityKind.Boss ? "BOSS " : "")}HP {_state.Health:0}/{_state.MaxHealth:0}\n{(IsAlive ? _damageText : "Повержен")}";
    }
    private static double Now() => Time.GetTicksMsec() / 1000d;

    internal static ArrayMesh BuildCone(float range, float halfAngle)
    {
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        const int segments = 12;
        for (var i = 0; i < segments; i++)
        {
            var from = -halfAngle + 2 * halfAngle * i / segments;
            var to = -halfAngle + 2 * halfAngle * (i + 1) / segments;
            surface.AddVertex(Vector3.Zero);
            surface.AddVertex(new Vector3(Mathf.Sin(from) * range, 0, Mathf.Cos(from) * range));
            surface.AddVertex(new Vector3(Mathf.Sin(to) * range, 0, Mathf.Cos(to) * range));
        }
        return surface.Commit();
    }
}
