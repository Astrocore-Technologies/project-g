using Godot;
namespace ProjectG.Animation;

/// <summary>Short stylized spark, one surface and no dynamic light or physics particles.</summary>
public partial class ImpactFlash : MeshInstance3D
{
    private static readonly SphereMesh Shape = new() { Radius=.085f, Height=.17f, RadialSegments=6, Rings=2 };
    public Color Tint { get; set; } = Colors.White;
    private double _life;
    public override void _Ready()
    { Mesh=Shape; MaterialOverride=new StandardMaterial3D { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor=Tint }; CastShadow=ShadowCastingSetting.Off; }
    public override void _Process(double delta)
    { _life+=delta; Scale=Vector3.One*Math.Max(.01f,1-(float)_life*5); if(_life>.2)QueueFree(); }
}
