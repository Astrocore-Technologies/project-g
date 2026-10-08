using Godot;
using ProjectG.Regions;
using ProjectG.Regions.Authoring;

namespace ProjectG.Tests.Regions;

public partial class RiverLandingBlockoutSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var region = GD.Load<PackedScene>("res://Scenes/Regions/RiverLanding/RiverLandingBlockout.tscn")
                .Instantiate<RegionRoot>();
            AddChild(region);
            var errors = RegionAuthoringValidation.Validate(region);
            Require(errors.Count == 0, string.Join("; ", errors));
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var space = region.GetWorld3D().DirectSpaceState;
            bool Hits(Vector3 point, uint mask) => space.IntersectRay(
                PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 8, point - Vector3.Up * 2, mask)).Count != 0;
            var preview = region.GetNode<BlockoutPreview>("Preview");
            for (var i = 0; i < 6; i++)
            {
                preview.ShowPoint(i);
                Require(preview.Camera.Projection == Camera3D.ProjectionType.Perspective &&
                    Mathf.IsEqualApprox(preview.Camera.Fov, 50), "Gameplay inspection camera mismatch.");
                var point = preview.Viewpoints.GetChild<Node3D>(i).GlobalPosition;
                Require(Hits(point, 1) && !Hits(point, 2), $"Viewpoint {i + 1} has no clear ground.");
            }
            Require(Hits(new Vector3(24, 0, -10), 1), "Bridge deck missing collision.");
            Require(!Hits(new Vector3(24, 0, 5), 1), "Walkable ground incorrectly covers river.");

            // Verify authored road centrelines against actual blockout blockers, not gameplay pathfinding.
            foreach (var road in region.GetNode("Terrain/Roads").GetChildren().OfType<MeshInstance3D>())
            {
                if (road.Mesh is not BoxMesh mesh) continue;
                for (var z = -mesh.Size.Z / 2 + 0.5f; z < mesh.Size.Z / 2; z += 0.5f)
                {
                    var point = road.GlobalTransform * new Vector3(0, 0, z);
                    Require(Hits(point, 1) && !Hits(point, 2), $"Road blocked or unsupported: {road.Name} at {point}.");
                }
            }
            preview.ShowOverview();
            if (OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Require(GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/river-landing-overview.png") == Error.Ok,
                    "Overview capture failed.");
                foreach (var (index, name) in new[] { (0, "yard"), (3, "grove"), (4, "bridge") })
                {
                    preview.ShowPoint(index);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    Require(GetViewport().GetTexture().GetImage().SavePng($"res://../.artifacts/river-landing-{name}.png") == Error.Ok,
                        "Gameplay camera capture failed.");
                }
            }
            region.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!GodotObject.IsInstanceValid(region), "Blockout subtree leaked.");
            GD.Print("RIVER_LANDING_BLOCKOUT_OK: authoring, six cameras, road centrelines, river, bridge, lifecycle.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
