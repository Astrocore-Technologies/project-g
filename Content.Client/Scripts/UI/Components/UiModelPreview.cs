using Godot;
namespace ProjectG.UI;

/// <summary>Isolated presentation world. Accepts an explicitly supplied visual, never a player/gameplay scene.</summary>
public partial class UiModelPreview : SubViewportContainer
{
    private SubViewport _viewport=null!;
    private Node3D _pivot=null!;
    private Camera3D _camera=null!;
    private Node3D? _visual;
    private float _zoom=5,_minZoom=2,_maxZoom=8;
    private bool _dragging;
    public override void _Ready() => Build();
    private void Build()
    {
        if(_viewport is not null) return;
        Stretch=true; CustomMinimumSize=new(180,240); MouseFilter=MouseFilterEnum.Stop;
        _viewport=new SubViewport { OwnWorld3D=true,TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.WhenVisible,GuiDisableInput=true,Size=new(360,480) }; AddChild(_viewport);
        var world=new Node3D(); _viewport.AddChild(world);
        world.AddChild(new WorldEnvironment { Environment=new Godot.Environment { BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new("293d50"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.65f } });
        world.AddChild(new DirectionalLight3D { RotationDegrees=new(-35,-30,0),LightEnergy=1.2f,ShadowEnabled=false });
        _pivot=new Node3D(); world.AddChild(_pivot); _camera=new Camera3D { Fov=35,Position=new(0,1.2f,_zoom),Current=true }; world.AddChild(_camera);
        GuiInput+=HandleInput; Resized+=ResizePreview; ResizePreview();
    }
    public void SetVisual(PackedScene? visual)
    {
        Build(); ClearVisual(); if(visual is null) return;
        var node=visual.Instantiate();
        if(node is not Node3D model) { node.Free(); GD.PushError("UI model preview expects a visual-only Node3D scene."); return; }
        _visual=model; _pivot.AddChild(model);
    }
    public void SetMesh(Mesh? mesh,Material? material)
    {
        Build(); ClearVisual(); if(mesh is null) return;
        var bounds=mesh.GetAabb(); var center=bounds.Position+bounds.Size/2;
        _visual=new MeshInstance3D { Mesh=mesh,MaterialOverride=material,Position=-center }; _pivot.AddChild(_visual);
        _zoom=Math.Max(.6f,bounds.Size.Length()*1.65f); _minZoom=_zoom*.55f; _maxZoom=_zoom*2.5f; _camera.Position=new(0,0,_zoom);
    }
    private void ClearVisual() { if(_visual is not null) { _pivot.RemoveChild(_visual); _visual.QueueFree(); _visual=null; } _pivot.Rotation=Vector3.Zero; }
    private void ResizePreview() { if(_viewport is not null) StretchShrink=Math.Max(1,(int)Math.Ceiling(Math.Max(Size.X,Size.Y)/960f)); }
    private void HandleInput(InputEvent ev)
    {
        if(ev is InputEventMouseButton button)
        {
            if(button.ButtonIndex==MouseButton.Left) _dragging=button.Pressed;
            if(button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { _zoom=Math.Clamp(_zoom*(button.ButtonIndex==MouseButton.WheelUp?.88f:1.12f),_minZoom,_maxZoom); _camera.Position=new(0,_camera.Position.Y,_zoom); }
            AcceptEvent();
        }
        else if(ev is InputEventMouseMotion motion && _dragging && Input.IsMouseButtonPressed(MouseButton.Left))
        { _pivot.RotateY(-motion.Relative.X*.012f); AcceptEvent(); }
    }
    public override void _ExitTree() { GuiInput-=HandleInput; Resized-=ResizePreview; }
}
