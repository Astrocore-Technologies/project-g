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

    public void Initialize(CombatState state, NetworkClient network, bool local)
    {
        _actor = GetParent<Node3D>();
        _network = network;
        _local = local;
        _spawnTick = state.ServerTick;
        AddChild(_healthLabel);
        AddChild(_slash);
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
        if (!_local || !IsAlive || @event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse ||
            Now() < _nextAttackAt || _pending != 0)
            return;
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
        if (++_sequence == 0)
            ++_sequence;
        _pending = _sequence;
        _pendingSince = Now();
        _nextAttackAt = Now() + _state.AttackInterval;
        ShowSwing(new(_actor.GlobalPosition.X, _actor.GlobalPosition.Z), direction, _state.Range, predicted: true);
        _network?.SendAttack(new AttackCommand(_sequence, (_actor as PlayerController)?.ClientTick ?? 0, direction));
        GetViewport().SetInputAsHandled();
    }

    public void Confirm(AttackEvent action)
    {
        if (!MovementSimulation.IsSequenceNewer(action.ServerTick, _spawnTick) ||
            !MovementSimulation.IsSequenceNewer(action.Sequence, _lastAttackSequence))
            return;
        _lastAttackSequence = action.Sequence;
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
        _damageText = $"{(action.Critical ? "CRIT " : "")}-{action.Damage:0.0}";
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

    public override void _Process(double delta)
    {
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
        if (!IsAlive) { _slash.Visible = false; _slashRemaining = 0; }
        _healthLabel.Text = $"HP {_state.Health:0.0}/{_state.MaxHealth:0.0}\n{(IsAlive ? _damageText : "Defeated")}";
    }
    private static double Now() => Time.GetTicksMsec() / 1000d;

    private static ArrayMesh BuildCone(float range, float halfAngle)
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
