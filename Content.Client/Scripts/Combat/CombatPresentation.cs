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
    private PlayerController? _player;
    private CombatPresentation? _autoTarget;
    private double _chaseAt;
    private readonly MeshInstance3D _selection=new() { Visible=false,Position=new(0,-.94f,0) };
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
        AddToGroup("CombatTargets");
        if (_player is not null) _player.ManualMoveRequested+=CancelAutoAttack;
        _selection.Mesh=new CylinderMesh { TopRadius=.65f,BottomRadius=.65f,Height=.025f };
        _selection.MaterialOverride=new StandardMaterial3D { AlbedoColor=new(1,.65f,.12f,.45f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(_selection);
        _spawnTick = state.ServerTick;
        AddChild(_healthLabel);
        AddChild(_slash);
        AddChild(_areaVisual);
        _slash.MaterialOverride = _slashMaterial;
        ApplyState(state);
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
        if (!_local || !IsAlive || ProjectG.UI.GameUi.CharacterWindowOpen || @event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
            return;
        CancelAutoAttack();
        var selected=PickTarget(mouse.Position);
        if (selected is not null)
        { _autoTarget=selected; selected._selection.Visible=true; _chaseAt=0; GetViewport().SetInputAsHandled(); return; }
        if (Now() < _nextAttackAt || _pending != 0 || _player?.DefenseHeld==true) return;
        var camera = GetViewport().GetCamera3D();
        if (camera is null)
            return;
        var origin = camera.ProjectRayOrigin(mouse.Position);
        var ray = camera.ProjectRayNormal(mouse.Position);
        if (Mathf.Abs(ray.Y) < 0.0001f)
            return;
        var distance = -origin.Y / ray.Y;
        if (distance <= 0)
            return;
        var point = origin + ray * distance;
        var offset = new NumericsVector2(point.X - _actor.GlobalPosition.X, point.Z - _actor.GlobalPosition.Z);
        if (offset.LengthSquared() < 0.000001f)
            return;
        var direction = NumericsVector2.Normalize(offset);
        Swing(direction);
        GetViewport().SetInputAsHandled();
    }
    private void Swing(NumericsVector2 direction,NetworkEntityId target=default)
    {
        if (++_sequence == 0)
            ++_sequence;
        _pending = _sequence;
        _pendingSince = Now();
        _nextAttackAt = Now() + _state.AttackInterval;
        ShowSwing(new(_actor.GlobalPosition.X, _actor.GlobalPosition.Z), direction, _state.Range, predicted: true);
        _network?.SendAttack(new AttackCommand(_sequence, _player?.ClientTick ?? 0, direction,target));
    }
    private CombatPresentation? PickTarget(Vector2 mouse)
    {
        var camera=GetViewport().GetCamera3D(); if(camera is null) return null;
        CombatPresentation? found=null; var nearest=float.MaxValue;
        // Click-time search over rendered AOI actors, never a per-frame world scan.
        foreach(var node in GetTree().GetNodesInGroup("CombatTargets"))
        {
            if(node is not CombatPresentation candidate || candidate==this || !candidate.IsAlive || camera.IsPositionBehind(candidate._actor.GlobalPosition)) continue;
            var a=camera.UnprojectPosition(candidate._actor.GlobalPosition+Vector3.Up*.8f);
            var b=camera.UnprojectPosition(candidate._actor.GlobalPosition-Vector3.Up*.8f);
            var segment=b-a; var t=Math.Clamp((mouse-a).Dot(segment)/Math.Max(.001f,segment.LengthSquared()),0,1);
            var distance=mouse.DistanceTo(a+segment*t);
            if(distance>Math.Clamp(segment.Length()*.4f,12,32) || distance>=nearest) continue;
            nearest=distance; found=candidate;
        }
        return found;
    }
    public void CancelAutoAttack()
    { if(GodotObject.IsInstanceValid(_autoTarget)) _autoTarget!._selection.Visible=false; _autoTarget=null; }
    public override void _ExitTree()
    { CancelAutoAttack(); if(_player is not null) _player.ManualMoveRequested-=CancelAutoAttack; }
    private void UpdateAutoAttack()
    {
        if (_autoTarget is null || _player is null) return;
        if (!GodotObject.IsInstanceValid(_autoTarget) || !_autoTarget.IsInsideTree() || !_autoTarget.IsAlive || !IsAlive || !GetWindow().HasFocus())
        { CancelAutoAttack(); _player.StopMovement(); return; }
        if(ProjectG.UI.GameUi.CharacterWindowOpen) { _player.StopMovement(); return; }
        if (_player.DefenseHeld || _player.IsDashing) return;
        var targetPosition=_autoTarget._actor.GlobalPosition; var point=new NumericsVector2(targetPosition.X,targetPosition.Z);
        var offset=point-_player.PredictedPosition;
        var inRange=offset.LengthSquared()<=_state.Range*_state.Range*.90f && _player.CanReachDirectly(point);
        if(Now()>=_chaseAt)
        { _chaseAt=Now()+.15; if(inRange) _player.StopMovement(); else if(!_player.MoveTo(point)) { CancelAutoAttack(); _player.StopMovement(); return; } }
        if(inRange && offset.LengthSquared()>.000001f && _pending==0 && Now()>=_nextAttackAt)
            Swing(NumericsVector2.Normalize(offset),_autoTarget._state.EntityId);
    }

    public void Confirm(AttackEvent action)
    {
        if (!MovementSimulation.IsSequenceNewer(action.ServerTick, _spawnTick) ||
            !MovementSimulation.IsSequenceNewer(action.Sequence, _lastAttackSequence))
            return;
        _lastAttackSequence = action.Sequence;
        if (action.Sequence == _areaSequence && _state.Kind == CombatEntityKind.Boss) return;
        if (_local && _pending == action.Sequence)
            _pending = 0;
        // Replace/restart the same visual with authoritative origin; do not apply damage twice.
        ShowSwing(action.Origin, action.Direction, action.Range, predicted: false);
    }

    public void ApplyResult(AttackResult result)
    {
        if (!_local || result.Sequence != _pending)
            return;
        _pending = 0;
        if (result.Outcome != AttackOutcome.Accepted)
        {
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
        _damageText = action.Guard==GuardImpact.Parried ? "ПАРИРОВАНИЕ" : $"{(action.Guard==GuardImpact.Blocked ? "БЛОК " : action.Critical ? "CRIT " : "")}-{action.Damage:0}";
        _damageRemaining = 0.6;
        UpdateLabel();
    }

    public void ApplyAbilityDamage(AbilityHit hit)
    {
        if (hit.ServerTick != _state.ServerTick && !MovementSimulation.IsSequenceNewer(hit.ServerTick, _state.ServerTick)) return;
        if (hit.TargetHealth > _state.MaxHealth) return;
        _state = _state with { Health = hit.TargetHealth, ServerTick = hit.ServerTick };
        _damageText = $"-{hit.Damage:0.0}"; _damageRemaining = 0.6;
        UpdateLabel();
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
        _areaVisual.GlobalPosition = new(value.Center.X, 0.12f, value.Center.Y);
        _areaVisual.Visible = true; _areaRemaining = value.RemainingSeconds;
    }

    public override void _Process(double delta)
    {
        if (_local) UpdateAutoAttack();
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
            _pending = 0;
            _slash.Visible = false;
        }
    }

    private void ShowSwing(NumericsVector2 origin, NumericsVector2 direction, float range, bool predicted)
    {
        _slashMaterial.AlbedoColor = predicted ? new Color(0.2f, 0.6f, 1, 0.4f) : new Color(1, 0.8f, 0.2f, 0.5f);
        _slash.GlobalPosition = new Vector3(origin.X, 0.2f, origin.Y);
        _slash.Rotation = new Vector3(0, Mathf.Atan2(direction.X, direction.Y), 0);
        _slash.Visible = true;
        _slashRemaining = 0.15;
    }

    private void UpdateLabel()
    {
        if (!IsAlive) { _slash.Visible = false; _slashRemaining = 0; _areaVisual.Visible = false; _areaRemaining = 0; }
        _healthLabel.Text = _local ? _damageText : $"{(_state.Kind == CombatEntityKind.Boss ? "BOSS " : "")}HP {_state.Health:0.0}/{_state.MaxHealth:0.0}\n{(IsAlive ? _damageText : "Defeated")}";
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
