using Godot;
using ProjectG.Regions;

namespace ProjectG.Tests.Camera;

/// <summary>Editable scene tests instance isolation, real fade materials and lifecycle restoration.</summary>
public partial class OcclusionSmoke : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var camera = GetNode<Camera3D>("Camera");
            var region = GetNode<Node3D>("Region");
            var wall = region.GetNode<MeshInstance3D>("Wall"); var roof = region.GetNode<MeshInstance3D>("Roof");
            var neighbour = region.GetNode<MeshInstance3D>("Neighbour"); var painted = region.GetNode<MeshInstance3D>("Painted");
            var original = wall.MaterialOverride;
            var originalPainted = (ShaderMaterial)painted.MaterialOverride;
            var body = wall.GetNode<StaticBody3D>("Body"); var collision = body.GetNode<CollisionShape3D>("Collision");
            var shape = collision.Shape;
            var presentation = new OcclusionPresentation(); AddChild(presentation);
            presentation.Initialize(GetNode<Node3D>("Player"), region);
            Check(presentation.IndexedMeshCount == 5 && presentation.FadedMeshCount == 4, "Wrong spatial candidates; include a thin beam across the torso");
            Check(wall.MaterialOverride != original && neighbour.MaterialOverride == original, "Shared instance was changed");
            Check(((BaseMaterial3D)original).AlbedoColor.A == 1, "Authored alpha was changed");
            Check(Mathf.Abs(((BaseMaterial3D)wall.MaterialOverride).AlbedoColor.A - .2f) < .0001f, "Wall opacity must be 20%");
            Check(Mathf.Abs(((BaseMaterial3D)roof.GetActiveMaterial(0)).AlbedoColor.A - .2f) < .0001f, "Surface material was not faded");
            Check(roof.MaterialOverride is null && roof.GetSurfaceOverrideMaterial(0) is not null, "Lost surface-specific override");
            Check(((ShaderMaterial)painted.MaterialOverride).Shader != originalPainted.Shader, "Authored shader was mutated");
            Check(((ShaderMaterial)painted.MaterialOverride).GetShaderParameter("wood_paint").AsGodotObject() ==
                originalPainted.GetShaderParameter("wood_paint").AsGodotObject(), "Fade lost the authored texture");
            Check(Mathf.Abs(((ShaderMaterial)painted.MaterialOverride).GetShaderParameter("camera_opacity").AsSingle() - .2f) < .0001f, "Painted opacity must be 20%");
            Check(body.CollisionLayer == 4 && !collision.Disabled && collision.Shape == shape, "Fade changed physical collision");
            var variant = wall.MaterialOverride;
            var start = camera.Position; var focus = GetNode<Node3D>("Player");
            focus.Position = new(100, 0, 0); camera.Position = start + focus.Position; presentation.Refresh();
            Check(presentation.FadedMeshCount == 0 && wall.MaterialOverride == original && roof.GetSurfaceOverrideMaterial(0) is null,
                "Original override was not restored after the camera moved");
            focus.Position = Vector3.Zero; camera.Position = start; presentation.Refresh();
            Check(wall.MaterialOverride == variant, "Fade material was recreated on each transition");
            presentation.SetEnabled(false);
            Check(wall.MaterialOverride == original && painted.MaterialOverride == originalPainted, "Disable must restore instances");
            presentation.SetEnabled(true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // The same scene can be launched with rendering for a reproducible visual check.
            if (DisplayServer.GetName() != "headless")
            {
                var faded = await Capture("occlusion");
                var pixel = camera.UnprojectPosition(new(0, .85f, 0));
                pixel *= new Vector2(faded.GetWidth(), faded.GetHeight()) / GetViewport().GetVisibleRect().Size;
                var own = faded.GetPixel((int)pixel.X, (int)pixel.Y);
                Check(own.R > own.G, "Own red avatar is not visible through faded geometry");
                presentation.SetEnabled(false);
                var opaque = await Capture("occlusion-opaque");
                var blocked = opaque.GetPixel((int)pixel.X, (int)pixel.Y);
                Check(blocked.R < blocked.G, "Opaque baseline does not actually cover the avatar");
                presentation.SetEnabled(true);
                GD.Print($"OCCLUSION_RENDER_OK: own pixel faded={own}, opaque={blocked}.");
            }
            presentation.GetParent().RemoveChild(presentation); presentation.Free();
            Check(wall.MaterialOverride == original && painted.MaterialOverride == originalPainted && roof.GetSurfaceOverrideMaterial(0) is null,
                "Region/player cleanup must restore original resources");
            if (OS.GetCmdlineUserArgs().Contains("--real-region")) await CheckBeech();
            GD.Print("OCCLUSION_SMOKE_OK: 20% wall/roof/painted opacity, shared-material isolation, collision, spatial restoration, reuse and exit.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private async Task<Image> Capture(string name)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var output = ProjectSettings.GlobalizePath($"res://../.artifacts/movement-controls-m3/{name}.png");
        Check(image.SavePng(output) == Error.Ok, "Cannot save rendered verification");
        return image;
    }

    private async Task CheckBeech()
    {
        var region = GD.Load<PackedScene>("res://Scenes/Regions/Linvel/BeechValley.tscn").Instantiate<Node3D>();
        var terrain = region.GetNode<Node3D>("Geometry/Terrain");
        terrain.ProcessMode = ProcessModeEnum.Disabled;
        AddChild(region);
        var camera = GetNode<Camera3D>("Camera"); camera.MakeCurrent(); terrain.Call("set_camera", camera);
        if (DisplayServer.GetName() != "headless") terrain.ProcessMode = ProcessModeEnum.Inherit;
        var focus = GetNode<Node3D>("Player");
        var presentation = new OcclusionPresentation(); AddChild(presentation);
        presentation.Initialize(focus, region);
        var samples = 0; var faded = 0;
        foreach (var place in region.GetNode<Node3D>("Places").GetChildren().OfType<Node3D>())
        {
            foreach (var asset in place.GetChildren().OfType<Node3D>())
            {
                if (asset.Name == "Center") continue;
                focus.GlobalPosition = asset.GlobalPosition;
                camera.GlobalPosition = focus.GlobalPosition + new Vector3(0, 14.28148f, 10);
                presentation.Refresh(); samples++; faded += presentation.FadedMeshCount;
                if (DisplayServer.GetName() != "headless")
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    if (asset.Name == "TimberWorkingBay") await Capture("beech-occlusion");
                }
            }
        }
        Check(presentation.IndexedMeshCount > 100 && faded > 0, "Real forest did not exercise occluders");
        Check(presentation.UnsupportedMaterialCount == 0, "Real forest has unsupported camera fade materials");
        GD.Print($"BEECH_OCCLUSION_OK: indexed={presentation.IndexedMeshCount}, viewpoints={samples}, total faded={faded}.");
        presentation.GetParent().RemoveChild(presentation); presentation.Free();
        region.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
