using System.Text.Json;
using Content.Shared.Navigation;
using Godot;

namespace ProjectG.Regions.Authoring;

/// <summary>Offline bake of editable public scenes. Runtime authority reads only the bounded exported data.</summary>
public partial class ExportSurfaces : Node
{
    private Rid map;
    public override async void _Ready()
    {
        try
        {
            var output = OS.GetCmdlineUserArgs().Single(a => a.StartsWith("--output="))[9..];
            var exports = new Dictionary<string, SurfaceGeometry>();
            foreach (var (id, path) in new[] { ("terrain_test", "res://Scenes/Regions/TerrainTest/TerrainTest.tscn"),
                ("dungeon_test", "res://Scenes/Regions/DungeonTest/DungeonTest.tscn") })
            {
                var scene = GD.Load<PackedScene>(path).Instantiate<Node3D>(); AddChild(scene);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var mesh = new NavigationMesh { AgentRadius = .45f, AgentHeight = 1.8f, AgentMaxClimb = .25f,
                    AgentMaxSlope = 45, CellSize = .15f, CellHeight = .05f, RegionMinSize = 0, RegionMergeSize = 0,
                    FilterWalkableLowHeightSpans = true, GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
                    GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.GroupsWithChildren, GeometrySourceGroupName = "surface_geometry" };
                map = NavigationServer3D.MapCreate(); NavigationServer3D.MapSetCellSize(map, mesh.CellSize);
                NavigationServer3D.MapSetCellHeight(map, mesh.CellHeight);
                var region = new NavigationRegion3D { Enabled = false }; region.SetNavigationMap(map); AddChild(region);
                region.NavigationMesh = mesh; region.BakeNavigationMesh(false);
                var polygons = new int[mesh.GetPolygonCount()][];
                for (var i = 0; i < polygons.Length; i++) polygons[i] = mesh.GetPolygon(i);
                var boxes = new List<SurfaceBox>();
                foreach (var node in scene.GetNode<Node3D>("Geometry").GetChildren())
                {
                    if (node is CsgBox3D csg) boxes.Add(new(V(csg.GlobalPosition), V(csg.Size), V(csg.GlobalRotation)));
                    else if (node is StaticBody3D body)
                    {
                        var collider = body.GetNode<CollisionShape3D>("Collision");
                        if (collider.Shape is not BoxShape3D box || !collider.GlobalBasis.Scale.IsEqualApprox(Vector3.One))
                            throw new InvalidDataException("Surface exporter requires unscaled box collision geometry.");
                        boxes.Add(new(V(collider.GlobalPosition), V(box.Size), V(collider.GlobalRotation)));
                    }
                    else throw new InvalidDataException("Unsupported surface collider.");
                }
                var geometry = new SurfaceGeometry { ScenePath = path, SourceHash = SurfaceGeometry.Fingerprint(File.ReadAllText(ProjectSettings.GlobalizePath(path))),
                    Vertices = mesh.GetVertices().Select(V).ToArray(), Polygons = polygons, Occluders = boxes.ToArray() };
                geometry.Seal(); geometry.Validate(); _ = new SurfaceNavigation(geometry); _ = geometry.Encode();
                exports.Add(id, geometry);
                GD.Print($"SURFACE_EXPORT {id} {geometry.Revision} polygons={polygons.Length}");
                region.Free(); scene.Free(); NavigationServer3D.FreeRid(map); map = default;
            }
            File.WriteAllText(output, JsonSerializer.Serialize(exports, SurfaceGeometry.Json));
            GD.Print("SURFACE_EXPORT_PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static SurfaceVertex V(Vector3 v) => new(v.X, v.Y, v.Z);
    public override void _ExitTree() { if (map.IsValid) NavigationServer3D.FreeRid(map); }
}
