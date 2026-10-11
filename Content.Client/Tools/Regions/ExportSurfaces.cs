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
                ("dungeon_test", "res://Scenes/Regions/DungeonTest/DungeonTest.tscn"),
                ("linvel_beech", "res://Scenes/Regions/Linvel/BeechValley.tscn") })
            {
                var scene = GD.Load<PackedScene>(path).Instantiate<Node3D>(); AddChild(scene);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                // Large outdoor scenes expose their bake resolution in editable scene metadata.
                var cellSize = scene.HasMeta("surface_cell_size") ? (float)scene.GetMeta("surface_cell_size") : .15f;
                var cellHeight = scene.HasMeta("surface_cell_height") ? (float)scene.GetMeta("surface_cell_height") : .05f;
                var mesh = new NavigationMesh { AgentRadius = .45f, AgentHeight = 1.8f, AgentMaxClimb = .25f,
                    AgentMaxSlope = 45, CellSize = cellSize, CellHeight = cellHeight, RegionMinSize = 0, RegionMergeSize = 0,
                    DetailSampleDistance = scene.HasMeta("surface_detail_distance") ? (float)scene.GetMeta("surface_detail_distance") : 6,
                    DetailSampleMaxError = scene.HasMeta("surface_detail_error") ? (float)scene.GetMeta("surface_detail_error") : 1,
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
                    // Terrain LOS comes from the exported walkable surface; a giant box would hide the valley.
                    // Decorative/non-opaque blockers still participate in the navigation bake.
                    if (node.IsInGroup("surface_terrain") || scene.HasMeta("region_id") && !node.IsInGroup("surface_occluder")) continue;
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
                // New outdoor regions pack pairs without changing the original triangulation.
                // Millimetre coordinate precision avoids long float tails in the bounded JSON.
                var vertices = mesh.GetVertices().Select(V).ToArray();
                if (scene.HasMeta("region_id"))
                {
                    polygons = ProjectG.NavigationStudy.SurfaceExportPacking.RemoveDegenerateFaces(vertices.Select(v => (v.X,v.Z)).ToArray(), polygons);
                    polygons = ProjectG.NavigationStudy.SurfaceExportPacking.PairTriangles(polygons);
                    for (var i = 0; i < vertices.Length; i++) vertices[i] = new((float)Math.Round(vertices[i].X,3),(float)Math.Round(vertices[i].Y,3),(float)Math.Round(vertices[i].Z,3));
                }
                var geometry = new SurfaceGeometry { ScenePath = path, SourceHash = SurfaceGeometry.Fingerprint(File.ReadAllText(ProjectSettings.GlobalizePath(path))),
                    Version = scene.HasMeta("region_id") ? 2 : 1, WalkableFacesOcclude = scene.HasMeta("region_id"), Vertices = vertices, Polygons = polygons, Occluders = boxes.ToArray() };
                GD.Print($"SURFACE_COUNTS {id} vertices={geometry.Vertices.Length} polygons={polygons.Length} boxes={boxes.Count}");
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
