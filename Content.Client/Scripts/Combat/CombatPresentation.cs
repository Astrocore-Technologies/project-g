using Content.Shared.Movement;
using Content.Shared.Combat;
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
    private readonly CombatAimBody _aimBody=new();
    private NumericsVector2 _bodyFacing=NumericsVector2.UnitY;
    private CombatControlPhase _bodyControl;
    private uint _bodyPoseTick,_bodyFacingTick;
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
    private float _basicDirectionY;
    private NumericsVector2 _basicYaw=NumericsVector2.UnitY;
    private ulong _lastAttackExecution;
    private ulong _phaseExecutionId;
    private BasicActionState? _ownBasic;
    private uint _ownBasicSequence;
    private uint _controlSequence;
    private bool _basicInputReady;
    private double _aimAt,_unconfirmedBasicUntil;
    private ulong _lastFeedbackId;
    private ulong _lastControlFeedbackId;
    private double _controlNoticeRemaining;
    private string _controlNotice="";
    private string _basicNotice="";
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
    public NumericsVector2 Facing=>_bodyFacing;

    public void Initialize(CombatState state, NetworkClient network, bool local)
    {
        _actor = GetParent<Node3D>();
        _network = network;
        _local = local;
        _player=local ? _actor as PlayerController : null;
        _swordAnimation=(_actor as PlayerController)?.SwordAnimation;
        if (_player is not null) _player.ManualMoveRequested+=CancelBasicAttack;
        _spawnTick = state.ServerTick;
        _bodyPoseTick=_bodyFacingTick=_spawnTick;
        AddChild(_healthLabel);
        AddChild(_slash);
        AddChild(_areaVisual);
        AddChild(_aimBody);
        if(_actor is CollisionObject3D movementBody)movementBody.CollisionLayer=CombatAimBody.MovementBodyLayer;
        if(_player is not null)_player.CombatAimBody=_aimBody;
        _slash.MaterialOverride = _slashMaterial;
        ApplyState(state);
        network.LifeReceived+=ApplyLife;
        if(local)
        {
            network.BasicInputReceived+=ApplyBasicInput;
            if(network.LatestBasicInput is {} input)ApplyBasicInput(input);
        }
        if(network.KnownLife.TryGetValue(state.EntityId,out var life))ApplyLife(life);
    }
    public void ApplyLife(ActorLifeState life)
    {
        if(life.EntityId!=_state.EntityId)return;
        _life=life;_lifeReceived=Now();_corpseVisibilityPending=life.Dead&&life.CorpseRemaining>0;
        _actor.Visible=!life.Dead || life.CorpseRemaining>0;
        _aimBody.Alive(!life.Dead);
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
        _aimBody.Configure(state.Body);_aimBody.Alive(IsAlive);
        if(state.BasicDelivery==BasicAttackDelivery.MeleeArc)_slash.Mesh = BuildCone(state.Range, state.HalfAngleRadians);
        else {_slash.Visible=false;_slashRemaining=0;}
        UpdateLabel();
    }

    public void ApplyBodyControl(CombatControlPhase control,uint tick)
    {
        if(tick!=_spawnTick && !MovementSimulation.IsSequenceNewer(tick,_spawnTick) ||
            tick!=_bodyPoseTick && !MovementSimulation.IsSequenceNewer(tick,_bodyPoseTick))return;
        _bodyPoseTick=tick;_bodyControl=control;_aimBody.Pose(control,_bodyFacing);
    }

    public void ApplyBodyFacing(AvatarState state)
    {
        if(state.ServerTick!=_spawnTick && !MovementSimulation.IsSequenceNewer(state.ServerTick,_spawnTick) ||
            state.ServerTick!=_bodyFacingTick && !MovementSimulation.IsSequenceNewer(state.ServerTick,_bodyFacingTick))return;
        _bodyFacingTick=state.ServerTick;_bodyFacing=_basicYaw=state.Facing;_aimBody.Pose(_bodyControl,_bodyFacing);
        _swordAnimation?.SetFacing(state.Facing);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_local || !IsAlive || PcInput.Blocked(this) || !(PcInput.Pressed(PcInput.Basic,@event) || PcInput.Pressed(PcInput.Manual,@event)))
            return;
        if (!_basicInputReady || _network?.RegionActive != true ||
            _player?.Control != CombatControlPhase.None || !TryBasicDirection((@event as InputEventMouseButton)?.Position, out var direction,out var directionY)) return;
        CancelBasicAttack();
        // A tap produces one swing, a held button requests server-timed repetition.
        // The existing Ctrl chord remains an explicit single-swing input.
        _continuousHeld = PcInput.Pressed(PcInput.Basic,@event);
        Swing(direction,directionY, _continuousHeld ? BasicAttackMode.BeginContinuous : BasicAttackMode.Manual);
        GetViewport().SetInputAsHandled();
    }

    public override void _Input(InputEvent @event)
    {
        // Receive release even if UI consumed the press/focus moved afterwards.
        if (_local && (PcInput.Released(PcInput.Basic,@event) || PcInput.Released(PcInput.Manual,@event))) CancelBasicAttack();
        if(_local && IsAlive && _network?.RegionActive==true && !PcInput.Blocked(this) &&
            PcInput.Pressed(PcInput.Cancel,@event) && _ownBasicSequence!=0)
        {
            CancelBasicAttack();
            SendBasicControl(BasicControlKind.Cancel,_basicDirection);
            GetViewport().SetInputAsHandled();
        }
    }

    private bool TryBasicDirection(Vector2? screen, out NumericsVector2 direction,out float directionY)
    {
        direction = _basicDirection;directionY=_basicDirectionY;
        if (_player is null || !_player.TryCursorCombatPoint(out var point, out var height, screen)) return false;
        var offset = point - _player.PredictedPosition;
        var y=height-_player.PredictedFoot.Y-_state.BasicOriginHeight;
        var length=MathF.Sqrt(offset.LengthSquared()+y*y);
        if(length<.0001f)return false;
        direction=offset/length;directionY=y/length;
        return true;
    }

    private NumericsVector2 BasicYaw(NumericsVector2 direction)
    {if(direction!=NumericsVector2.Zero)_basicYaw=NumericsVector2.Normalize(direction);return _basicYaw;}

    private uint NextSequence() { if (++_sequence == 0) ++_sequence; return _sequence; }

    public void ApplyBasicInput(BasicInputState state)
    {
        if(state.ActorId!=_state.EntityId)return;
        if(_sequence==0 || MovementSimulation.IsSequenceNewer(state.LastInputSequence,_sequence))_sequence=state.LastInputSequence;
        if(_controlSequence==0 || MovementSimulation.IsSequenceNewer(state.LastControlSequence,_controlSequence))_controlSequence=state.LastControlSequence;
        _basicInputReady=true;
    }

    private void SendBasicControl(BasicControlKind kind,NumericsVector2 direction)
    {
        if(_ownBasicSequence==0)return;
        if(++_controlSequence==0)++_controlSequence;
        _network?.SendBasicControl(new(_controlSequence,_ownBasicSequence,_ownBasic?.ExecutionId??0,kind,direction,_basicDirectionY));
    }

    private void Swing(NumericsVector2 direction,float directionY, BasicAttackMode mode)
    {
        _pending = NextSequence();
        _ownBasicSequence=_pending;_ownBasic=null;
        _unconfirmedBasicUntil=Now()+_state.AttackInterval+2;
        _aimAt=Now()+NetworkConstants.ContinuousAttackHeartbeatSeconds;
        _pendingSince = Now();
        _basicDirection = direction;
        _basicDirectionY=directionY;
        _heartbeatAt = Now() + NetworkConstants.ContinuousAttackHeartbeatSeconds;
        if (Now() >= _nextAttackAt)
        {
            _nextAttackAt = Now() + _state.AttackInterval;
            if(_state.BasicDelivery==BasicAttackDelivery.MeleeArc)
            {
                _swordAnimation?.PredictBasic(_sequence, BasicYaw(direction), _state.AttackInterval,directionY);
                ShowSwing(new(_actor.GlobalPosition.X, _actor.GlobalPosition.Z), direction, _state.Range, predicted: true,directionY:directionY);
            }
            else {_basicNotice="Подготовка выстрела";UpdateLabel();}
        }
        _network?.SendAttack(new(_sequence, _player?.ClientTick ?? 0, direction, mode,directionY));
    }

    public void CancelBasicAttack()
    {
        if (!_continuousHeld) return;
        _continuousHeld = false;
        _network?.SendAttack(new(NextSequence(), _player?.ClientTick ?? 0, _basicDirection, BasicAttackMode.EndContinuous,_basicDirectionY));
    }

    public override void _ExitTree()
    {
        CancelBasicAttack();
        if (_network is not null){_network.LifeReceived-=ApplyLife;_network.BasicInputReceived-=ApplyBasicInput;}
        if (_player is not null)
        {
            _player.ManualMoveRequested-=CancelBasicAttack;
            if(ReferenceEquals(_player.CombatAimBody,_aimBody))_player.CombatAimBody=null;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if(_local && _ownBasicSequence!=0 && _player is not null && _network?.RegionActive==true && IsAlive)
        {
            if(_ownBasic is null && Now()>_unconfirmedBasicUntil)_ownBasicSequence=0;
            else if((_ownBasic is null || _ownBasic is {Phase:CombatActionPhase.Preparation,CanTurn:true}) && Now()>=_aimAt)
            {
                _aimAt=Now()+NetworkConstants.ContinuousAttackHeartbeatSeconds;
                if(TryBasicDirection(null,out var aim,out var y) && (NumericsVector2.DistanceSquared(aim,_basicDirection)+MathF.Pow(y-_basicDirectionY,2))>.000001f)
                {_basicDirection=aim;_basicDirectionY=y;SendBasicControl(BasicControlKind.Aim,aim);_swordAnimation?.AimBasic(_ownBasicSequence,BasicYaw(aim),y);}
            }
        }
        if (!_local || !_continuousHeld || _player is null) return;
        if (!PcInput.Held(PcInput.Basic) || !IsAlive || !GetWindow().HasFocus() ||
            _network?.RegionActive != true || ProjectG.UI.GameUi.GameplayModalOpen || _player.DefenseHeld ||
            _player.IsDashing || _player.Control != CombatControlPhase.None || _player.RecoveryRemaining > 0)
        { CancelBasicAttack(); return; }
        if (Now() < _heartbeatAt) return;
        _heartbeatAt = Now() + NetworkConstants.ContinuousAttackHeartbeatSeconds;
        if (TryBasicDirection(null, out var direction,out var directionY)){_basicDirection = direction;_basicDirectionY=directionY;}
        _network.SendAttack(new(NextSequence(), _player.ClientTick, _basicDirection, BasicAttackMode.UpdateContinuous,_basicDirectionY));
    }

    public void Confirm(AttackEvent action)
    {
        if (!IsAlive || !MovementSimulation.IsSequenceNewer(action.ServerTick, _spawnTick) ||
            (action.ExecutionId != 0 ? action.ExecutionId <= _lastAttackExecution :
                !MovementSimulation.IsSequenceNewer(action.Sequence, _lastAttackSequence)))
            return;
        _lastAttackSequence = action.Sequence;
        _lastAttackExecution = action.ExecutionId;
        if (_local && action.Delivery==BasicAttackDelivery.MeleeArc && action.ExecutionId!=_phaseExecutionId) _nextAttackAt = Now() + _state.AttackInterval;
        if (action.Sequence == _areaSequence && _state.Kind == CombatEntityKind.Boss) return;
        if (_local && _pending == action.Sequence)
            _pending = 0;
        // Replace/restart the same visual with authoritative origin; do not apply damage twice.
        if(action.Delivery==BasicAttackDelivery.MeleeArc)
        {
            ShowSwing(action.Origin, action.Direction, action.Range, predicted: false,action.DirectionY,action.OriginHeight,
                action.HalfAngleRadians>0?action.HalfAngleRadians:null);
            _swordAnimation?.ConfirmBasic(action);
        }
        if(!action.TargetId.IsValid)
        { _damageText="ПРОМАХ"; _damageRemaining=.6; UpdateLabel(); }
    }

    public void ApplyBasicAction(BasicActionState state)
    {
        if(state.ActorId!=_state.EntityId)return;
        _phaseExecutionId=state.ExecutionId;
        _swordAnimation?.ApplyBasic(state);
        _basicNotice=state.Delivery!=BasicAttackDelivery.Projectile ? "" : state.Phase switch
        {CombatActionPhase.Preparation=>"Подготовка выстрела",CombatActionPhase.Active=>"Выпуск",CombatActionPhase.Recovery=>"Восстановление",_=>""};
        UpdateLabel();
        if(!_local)return;
        if(state.Phase==CombatActionPhase.Finished)
        {
            if(_ownBasicSequence==state.Sequence && (_ownBasic is null || _ownBasic.Value.ExecutionId==state.ExecutionId))
            {_ownBasic=null;_ownBasicSequence=0;}
        }
        else if(_pending==0 || state.Sequence==_pending || _ownBasicSequence==state.Sequence)
        {
            _ownBasic=state;_ownBasicSequence=state.Sequence;_basicDirection=state.Direction;_basicDirectionY=state.DirectionY;
        }
        if(state.Phase!=Content.Shared.Combat.CombatActionPhase.Finished && state.Sequence==_pending)_pending=0;
        if(state.Phase==Content.Shared.Combat.CombatActionPhase.Preparation)
            _nextAttackAt=Now()+state.RemainingSeconds+state.ActiveSeconds+state.RecoverySeconds;
    }

    public void ApplyResult(AttackResult result)
    {
        if(_local && result.Sequence==_ownBasicSequence && result.Outcome is AttackOutcome.Cancelled or AttackOutcome.CancelDenied)
        {
            if(result.Outcome==AttackOutcome.Cancelled)
            {_basicNotice="";_swordAnimation?.RejectBasic(result.Sequence);_ownBasic=null;_ownBasicSequence=0;_pending=0;UpdateLabel();}
            else {_damageText="Сейчас нельзя отменить удар";_damageRemaining=.6;UpdateLabel();}
            return;
        }
        if (!_local || result.Sequence != _pending)
            return;
        if(result.Outcome==AttackOutcome.Buffered)
        {
            _swordAnimation?.RejectBasic(result.Sequence); _slash.Visible=false; _slashRemaining=0;
            _damageText="Действие подготовлено";_damageRemaining=.15;UpdateLabel();return;
        }
        _pending = 0;
        if (result.Outcome != AttackOutcome.Accepted)
        {
            _basicNotice="";
            if(_ownBasicSequence==result.Sequence){_ownBasicSequence=0;_ownBasic=null;}
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
        _damageText = HitFeedback(action.Damage,action.Critical,action.Evaded,action.Guard)+HitContextText.Suffix(action.Tags);
        _damageRemaining = 0.6;
        UpdateLabel();
    }

    public void ApplyAbilityDamage(AbilityHit hit)
    {
        if (hit.ServerTick != _state.ServerTick && !MovementSimulation.IsSequenceNewer(hit.ServerTick, _state.ServerTick)) return;
        if (hit.TargetHealth > _state.MaxHealth) return;
        _state = _state with { Health = hit.TargetHealth, ServerTick = hit.ServerTick };
        _damageText = HitFeedback(hit.Damage,hit.Critical,hit.Evaded,hit.Guard)+HitContextText.Suffix(hit.Tags); _damageRemaining = 0.6;
        UpdateLabel();
    }

    private static string HitFeedback(double damage,bool critical,bool evaded,GuardImpact guard)
    {
        if(evaded) return "УКЛОНЕНИЕ";
        if(guard==GuardImpact.Parried) return "ПАРИРОВАНИЕ";
        if(guard==GuardImpact.Dodged) return "УВОРОТ";
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
            CombatFeedbackKind.Dodge=>"УВОРОТ",
            _=>$"{(value.Critical?"CRIT ":"")}-{value.Amount:0}"
        };
        _damageRemaining=.8;UpdateLabel();
    }

    public void ApplyControlFeedback(ControlFeedback value)
    {
        if(value.TargetId!=_state.EntityId || value.EventId<=_lastControlFeedbackId ||
            value.ServerTick!=_spawnTick && !MovementSimulation.IsSequenceNewer(value.ServerTick,_spawnTick))return;
        _lastControlFeedbackId=value.EventId;
        var name=CombatJournalPresentation.ControlName(value.Control);
        _controlNotice=value.Change switch
        {
            ControlChangeKind.Applied=>$"КОНТРОЛЬ: {name} · {value.Duration:0.##} с",
            ControlChangeKind.Refreshed=>$"ОБНОВЛЁН: {name} · {value.Duration:0.##} с",
            ControlChangeKind.Expired=>$"ИСТЁК: {name}",
            ControlChangeKind.Rejected=>$"{(value.Mitigation==ControlMitigation.Immunity ? "ИММУНИТЕТ" : "СОПРОТИВЛЕНИЕ")}: {name}",
            _=>$"СНЯТ: {name}"
        };
        if(value.Change is ControlChangeKind.Applied or ControlChangeKind.Refreshed && value.Mitigation!=ControlMitigation.None)
            _controlNotice+=" · ослаблен";
        if((value.Mitigation&ControlMitigation.AdaptiveResistance)!=0)_controlNotice+=" · устойчивость к повторному контролю";
        if(value.Change is ControlChangeKind.Removed or ControlChangeKind.Expired && value.RemainingInstances>0)
            _controlNotice+=$" · ещё действует: {value.RemainingInstances}";
        _controlNoticeRemaining=1.2;UpdateLabel();
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
        _areaVisual.Mesh = new CylinderMesh { TopRadius = value.Radius, BottomRadius = value.Radius, Height = value.VolumeHeight>0?value.VolumeHeight:.035f };
        _areaVisual.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = value.Phase == NpcAreaPhase.Telegraph ? new Color(1, 0.45f, 0.1f, 0.12f) : new Color(1, 0.1f, 0.05f, 0.25f)
        };
        _areaVisual.GlobalPosition = new(value.Center.X, value.Height + (value.VolumeHeight>0?value.VolumeHeight*.5f:.12f), value.Center.Y);
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
        if(_controlNoticeRemaining>0 && (_controlNoticeRemaining-=delta)<=0)
        {_controlNotice="";UpdateLabel();}
        // Connection loss or a delayed ack cannot leave unlimited pending predictions.
        if (_pending != 0 && Now() - _pendingSince > 2)
        {
            _swordAnimation?.RejectBasic(_pending);
            _pending = 0;
            _slash.Visible = false;
        }
    }

    private void ShowSwing(NumericsVector2 origin, NumericsVector2 direction, float range, bool predicted,
        float directionY=0,float? originHeight=null,float? halfAngle=null)
    {
        _slashMaterial.AlbedoColor = predicted ? new Color(0.2f, 0.6f, 1, 0.4f) : new Color(1, 0.8f, 0.2f, 0.5f);
        var forward=new Vector3(direction.X,directionY,direction.Y).Normalized();
        var yaw=BasicYaw(direction);var right=new Vector3(yaw.Y,0,-yaw.X);
        var normal=forward.Cross(right).Normalized();
        _slash.Mesh=BuildCone(range,halfAngle??_state.HalfAngleRadians);
        _slash.GlobalTransform=new(new Basis(right,normal,forward),new(origin.X,originHeight??(_actor.GlobalPosition.Y-1+_state.BasicOriginHeight),origin.Y));
        _slash.Visible = true;
        _slashRemaining = 0.15;
    }

    private void UpdateLabel()
    {
        _aimBody.Alive(IsAlive);
        if (!IsAlive) { _basicNotice=""; _slash.Visible = false; _slashRemaining = 0; _areaVisual.Visible = false; _areaRemaining = 0; _swordAnimation?.Stop(); }
        var notice=(_basicNotice.Length>0?"\n"+_basicNotice:"")+(_controlNotice.Length>0 ? "\n"+_controlNotice : "");
        _healthLabel.Text = _local ? _damageText+notice : $"{(_state.Kind == CombatEntityKind.TrainingTarget ? "Манекен · " : _state.Kind == CombatEntityKind.Boss ? "BOSS " : "")}HP {_state.Health:0}/{_state.MaxHealth:0}\n{(IsAlive ? _damageText+notice : "Повержен")}";
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
