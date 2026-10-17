using Content.Shared.Navigation;
using Content.Shared.Network;
using Godot;
using ProjectG.Combat;

namespace ProjectG.Echoes;

/// <summary>Interpolated server actor; effects never predict damage.</summary>
public partial class EchoPresentation : NpcPresentation
{
    private MeshInstance3D _area = null!;
    private Vector3 _center;
    private double _flash;
    public void Initialize(EchoSpawn value, NavigationGrid grid)
    {
        base.Initialize(value.Position, value.ServerTick, grid, value.Height);
        var figure=ProjectG.Actors.ActorScenes.Load("EchoFigure").Instantiate<ProjectG.Actors.ActorVisual>();
        figure.Name="Figure";figure.Position=Vector3.Down;AddChild(figure);
        AddChild(new Label3D { Text = $"{value.Name} · Эхо {value.Slot}", Position = new(0, 1.3f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32 });
        _area = new MeshInstance3D { Visible = false, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(1, .8f, .1f) } };
        AddChild(_area);
    }
    public void BindCombat(CombatState value,NavigationGrid grid)=>base.Initialize(value,grid);
    public MeshInstance3D PreviewBody=>GetNode<MeshInstance3D>("Figure/Center/Pose/Body");
    public void Show(EchoAction value)
    {
        _flash = .25;
        if (value.Kind != EchoActionKind.Signature) return;
        _area.Mesh = new CylinderMesh { TopRadius = value.Radius, BottomRadius = value.Radius, Height = .03f };
        _center = new(value.Position.X, value.Height + .06f, value.Position.Y);
        _area.Visible = true;
    }
    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_flash <= 0) return;
        _area.GlobalPosition = _center;
        _flash -= delta;
        if (_flash <= 0) _area.Visible = false;
    }
}
