using Content.Shared.Network;
using Godot;

namespace ProjectG.Combat;

/// <summary>Presentation only: visual projectile travel never produces a local hit.</summary>
public partial class AbilityEffectVisual : Node3D
{
    private readonly MeshInstance3D _mesh = new();
    private AbilityEffectState _state;
    private double _remaining;
    // NC: one bounded ribbon surface per impact, with a short fade and no dynamic lights.
    private StandardMaterial3D? _material;
    private float _opacity;
    private double _life;
    public Node3D? Caster { get; set; }
    public System.Numerics.Vector2 AimFacing {get;set;}=System.Numerics.Vector2.UnitY;
    private bool _applied;
    private Vector3 _casterPosition;

    public override void _Ready() => AddChild(_mesh);

    public void Apply(AbilityEffectState state, bool predicted = false)
    {
        if(_applied && state.Phase is AbilityPhase.Charging or AbilityPhase.Channeling or AbilityPhase.Sustaining && _state.Phase==state.Phase && _state.EffectId==state.EffectId)
        {
            _state=state;_remaining=state.RemainingSeconds+.25;_casterPosition=new(state.Position.X,state.Height+1,state.Position.Y);
            Position=new(state.Position.X,state.Height+.12f,state.Position.Y);
            if(state.Form is AbilityForm.Projectile or AbilityForm.Melee)
            {
                var length=state.Form==AbilityForm.Projectile?1.2f:state.Area.Shape==AbilityAreaShape.Corridor?state.Area.Length:0;
                Position+=new Vector3(state.Direction.X,0,state.Direction.Y)*length*.5f;
                Rotation=new(0,Mathf.Atan2(state.Direction.X,state.Direction.Y),0);
            }
            if(state.Form==AbilityForm.Melee)PlaceMelee(state);
            if(state.Form==AbilityForm.GroundArea && state.GroundVolumeHeight>0)
                Position=new(state.Position.X,state.Height+state.GroundVolumeHeight*.5f,state.Position.Y);
            return;
        }
        _applied=true;
        _state = state;
        _casterPosition=new(state.Position.X,state.Height+1,state.Position.Y);
        _remaining = state.RemainingSeconds + 0.25;
        _life = _remaining;
        Position = new(state.Position.X, state.Height + (state.Phase == AbilityPhase.Flying ? 0 : .12f), state.Position.Y);
        var projectile = state.Phase == AbilityPhase.Flying;
        var radius = Math.Max(0.08f, state.Radius);
        Rotation = Vector3.Zero;
        _mesh.Mesh = projectile
            ? new SphereMesh { Radius = radius, Height = radius * 2 }
            : new CylinderMesh { TopRadius = radius, BottomRadius = radius,
                Height = state.Form==AbilityForm.GroundArea && state.GroundVolumeHeight>0 ? state.GroundVolumeHeight : .035f };
        if (state.Form == AbilityForm.Melee)
        {
            // The same public geometry drives aiming and confirmed melee telegraphs.
            if (state.Area.Shape == AbilityAreaShape.Corridor)
            {
                _mesh.Mesh = new BoxMesh { Size = new(state.Area.HalfWidth * 2, .035f, state.Area.Length) };
                Position = new(state.Position.X + state.Direction.X * state.Area.Length / 2, state.Height + .12f,
                    state.Position.Y + state.Direction.Y * state.Area.Length / 2);
            }
            else _mesh.Mesh = CombatPresentation.BuildCone(state.Area.Length > 0 ? state.Area.Length : radius,
                state.Area.HalfAngleRadians > 0 ? state.Area.HalfAngleRadians : MathF.PI);
            Rotation = new(0,Mathf.Atan2(state.Direction.X,state.Direction.Y),0);
        }
        if (state.Phase == AbilityPhase.Dash ||
            (state.Form == AbilityForm.Projectile && state.Phase is AbilityPhase.Telegraph or AbilityPhase.Charging))
        {
            var length = state.Phase == AbilityPhase.Dash
                ? System.Numerics.Vector2.Distance(state.Origin, state.Position) : 1.2f;
            _mesh.Mesh = new BoxMesh { Size = new(0.2f, 0.035f, Math.Max(0.05f, length)) };
            Position = new(state.Origin.X + state.Direction.X * length * 0.5f, state.OriginHeight + .12f,
                state.Origin.Y + state.Direction.Y * length * 0.5f);
            Rotation = new(0, Mathf.Atan2(state.Direction.X, state.Direction.Y), 0);
        }
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = predicted ? new Color(0.2f, 0.6f, 1, 0.4f)
                : state.Phase == AbilityPhase.Impact ? new Color(1, 0.25f, 0.1f, 0.8f)
                : projectile ? new Color(0.15f, 0.85f, 1, 0.9f) : new Color(1, 0.8f, 0.15f, 0.45f)
        };
        if(ProjectG.Animation.SkillEffectStyle.Get(state.AbilityId) is { } style)
        {
            _material.AlbedoColor = new Color(style.Color, predicted ? .22f : state.Phase == AbilityPhase.Impact ? .7f : .25f);
            if(state.Phase == AbilityPhase.Impact)
            {
                _mesh.Mesh = style.Ribbon(state.AbilityId == 28 ? .65f : radius);
                Position = new(state.Position.X,state.Height + style.Height,state.Position.Y);
            }
        }
        if(state.Form==AbilityForm.GroundArea && state.GroundVolumeHeight>0)
        {
            Position=new(state.Position.X,state.Height+state.GroundVolumeHeight*.5f,state.Position.Y);
            var color=_material.AlbedoColor;color.A=state.Phase==AbilityPhase.Impact ? .25f : .12f;_material.AlbedoColor=color;
        }
        _opacity = _material.AlbedoColor.A;
        _mesh.Visible=state.Phase != AbilityPhase.Recovery;
        if(state.Form==AbilityForm.Melee)PlaceMelee(state);
        _mesh.MaterialOverride = _material;
        _mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private void PlaceMelee(AbilityEffectState state)
    {
        var forward=new Vector3(state.Direction.X,state.DirectionY,state.Direction.Y);
        Position=new(state.Position.X,state.OriginHeight,state.Position.Y);
        if(state.Area.Shape==AbilityAreaShape.Corridor)Position+=forward*(state.Area.Length*.5f);
        var yaw=state.Direction==System.Numerics.Vector2.Zero?AimFacing:state.Direction;
        Rotation=new(-Mathf.Asin(Math.Clamp(state.DirectionY,-1,1)),Mathf.Atan2(yaw.X,yaw.Y),0);
    }

    public override void _Process(double delta)
    {
        if (_state.Phase is AbilityPhase.Telegraph or AbilityPhase.Charging && _state.Form is AbilityForm.Melee or AbilityForm.Recovery && GodotObject.IsInstanceValid(Caster))
        { Position+=Caster!.GlobalPosition-_casterPosition; _casterPosition=Caster.GlobalPosition; }
        if(_state.Phase!=AbilityPhase.Charging && (_state.Phase is not (AbilityPhase.Channeling or AbilityPhase.Sustaining) || _state.RemainingSeconds>0))
            if ((_remaining -= delta) <= 0) { QueueFree(); return; }
        if(_material is not null && _state.Phase == AbilityPhase.Impact)
        { var color=_material.AlbedoColor; color.A=_opacity*(float)(_remaining/Math.Max(.001,_life)); _material.AlbedoColor=color; }
        if (_state.Phase == AbilityPhase.Flying)
            Position += new Vector3(_state.Direction.X, _state.DirectionY, _state.Direction.Y) * _state.Speed * (float)delta;
    }
}
