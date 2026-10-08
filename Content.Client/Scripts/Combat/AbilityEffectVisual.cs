using Content.Shared.Network;
using Godot;

namespace ProjectG.Combat;

/// <summary>Presentation only: visual projectile travel never produces a local hit.</summary>
public partial class AbilityEffectVisual : Node3D
{
    private readonly MeshInstance3D _mesh = new();
    private AbilityEffectState _state;
    private double _remaining;

    public override void _Ready() => AddChild(_mesh);

    public void Apply(AbilityEffectState state, bool predicted = false)
    {
        _state = state;
        _remaining = state.RemainingSeconds + 0.25;
        Position = new(state.Position.X, 0.12f, state.Position.Y);
        var projectile = state.Phase == AbilityPhase.Flying;
        var radius = Math.Max(0.08f, state.Radius);
        Rotation = Vector3.Zero;
        _mesh.Mesh = projectile
            ? new SphereMesh { Radius = radius, Height = radius * 2 }
            : new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.035f };
        if (state.Phase == AbilityPhase.Dash ||
            (state.Form == AbilityForm.Projectile && state.Phase == AbilityPhase.Telegraph))
        {
            var length = state.Phase == AbilityPhase.Dash
                ? System.Numerics.Vector2.Distance(state.Origin, state.Position) : 1.2f;
            _mesh.Mesh = new BoxMesh { Size = new(0.2f, 0.035f, Math.Max(0.05f, length)) };
            Position = new(state.Origin.X + state.Direction.X * length * 0.5f, 0.12f,
                state.Origin.Y + state.Direction.Y * length * 0.5f);
            Rotation = new(0, Mathf.Atan2(state.Direction.X, state.Direction.Y), 0);
        }
        _mesh.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = predicted ? new Color(0.2f, 0.6f, 1, 0.4f)
                : state.Phase == AbilityPhase.Impact ? new Color(1, 0.25f, 0.1f, 0.8f)
                : projectile ? new Color(0.15f, 0.85f, 1, 0.9f) : new Color(1, 0.8f, 0.15f, 0.45f)
        };
    }

    public override void _Process(double delta)
    {
        if ((_remaining -= delta) <= 0) { QueueFree(); return; }
        if (_state.Phase == AbilityPhase.Flying)
            Position += new Vector3(_state.Direction.X, 0, _state.Direction.Y) * _state.Speed * (float)delta;
    }
}
