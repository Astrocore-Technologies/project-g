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
            AddChild(new Camera3D {Name="OfflineBakeCamera",Current=true});
            var exports = new Dictionary<string, SurfaceGeometry>();
            foreach (var (id, path) in new[] { ("terrain_test", "res://Scenes/Regions/TerrainTest/TerrainTest.tscn"),
                ("dungeon_test", "res://Scenes/Regions/DungeonTest/DungeonTest.tscn"),
                ("linvel_beech", "res://Scenes/Regions/Linvel/BeechValley.tscn"),
                ("linvel_gardens", "res://Scenes/Regions/Linvel/GardenValleys.tscn") })
            {
                var scene = GD.Load<PackedScene>(path).Instantiate<Node3D>(); AddChild(scene);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                PrepareTerrainInputs(scene);
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
                    polygons = RemoveBlockerTops(scene,vertices,polygons,cellSize,cellHeight);
                    var compact = ProjectG.NavigationStudy.SurfaceExportPacking.CompactVertices(polygons);
                    vertices = compact.SourceVertices.Select(i=>vertices[i]).ToArray();
                    polygons = compact.Polygons;
                    boxes = boxes.Select(b=>new SurfaceBox(Round(b.Center,3),Round(b.Size,3),Round(b.Rotation,6))).ToList();
                    polygons = ProjectG.NavigationStudy.SurfaceExportPacking.PairTriangles(polygons);
                    for (var i = 0; i < vertices.Length; i++) vertices[i] = new((float)Math.Round(vertices[i].X,3),(float)Math.Round(vertices[i].Y,3),(float)Math.Round(vertices[i].Z,3));
                }
                var geometry = new SurfaceGeometry { ScenePath = path, SourceHash = ProjectG.Authoring.SurfaceSourceFingerprint.Compute(ProjectSettings.GlobalizePath("res://"),path),
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
    // Terrain3D is the editable source. Its native height-map collision is not
    // parsed by Recast; use an exact temporary mesh only during the offline bake.
    // The temporary collider never enters the authored or runtime scene.
    private static void PrepareTerrainInputs(Node3D scene)
    {
        var geometry=scene.GetNode<Node3D>("Geometry");
        foreach(var terrain in geometry.GetChildren().Where(n=>n.GetClass()=="Terrain3D").ToArray())
        {
            if(terrain is not Node3D spatial || !spatial.GlobalTransform.IsEqualApprox(Transform3D.Identity))
                throw new InvalidDataException("Terrain3D must use an identity world transform.");
            var mesh=terrain.Call("bake_mesh",1,0).AsGodotObject() as Mesh
                ?? throw new InvalidDataException("Terrain3D returned no authored terrain mesh.");
            var faces=mesh.GetFaces();
            if(faces.Length==0) throw new InvalidDataException("Terrain3D has no walkable authored data.");
            terrain.Set("collision_mode",0);
            var body=new StaticBody3D {Name="OfflineTerrainBake",CollisionLayer=1};
            body.AddToGroup("surface_terrain"); geometry.AddChild(body);
            body.AddChild(new CollisionShape3D {Name="Collision",Shape=new ConcavePolygonShape3D {Data=faces}});
        }
    }
    private static SurfaceVertex V(Vector3 v) => new(v.X, v.Y, v.Z);
    // Same millimetre precision as terrain vertices; avoid unbounded float tails.
    private static SurfaceVertex Round(SurfaceVertex v,int digits) => new((float)Math.Round(v.X,digits),(float)Math.Round(v.Y,digits),(float)Math.Round(v.Z,digits));

    private static int[][] RemoveBlockerTops(Node3D scene,SurfaceVertex[] vertices,int[][] polygons,float cellSize,float cellHeight)
    {
        var caps = new List<(Transform3D Inverse,Vector3 Half)>();
        foreach (var node in scene.GetNode<Node3D>("Geometry").GetChildren())
        {
            if (node is not StaticBody3D body) continue;
            var upright = body.HasMeta("collision_part") && body.GetMeta("collision_part").AsString() is "trunk" or "post";
            var nonwalkable = body.HasMeta("surface_nonwalkable_top") && body.GetMeta("surface_nonwalkable_top").AsBool();
            if (!upright && !nonwalkable) continue;
            var collision = body.GetNode<CollisionShape3D>("Collision");
            caps.Add((collision.GlobalTransform.AffineInverse(),((BoxShape3D)collision.Shape).Size*.5f));
        }
        // Recast also bakes flat box tops. These particular visible parts are
        // upright blockers, never walkable platforms. Do not export their caps;
        // preserve terrain, bridge floors and the rest of the original triangles.
        return polygons.Where(p=>!caps.Any(cap=>p.All(i=>
        {
            var v=vertices[i]; var local=cap.Inverse*new Vector3(v.X,v.Y,v.Z);
            return Math.Abs(local.Y-cap.Half.Y)<=cellHeight*8f &&
                Math.Abs(local.X)<=cap.Half.X+cellSize && Math.Abs(local.Z)<=cap.Half.Z+cellSize;
        }))).ToArray();
    }
    public override void _ExitTree() { if (map.IsValid) NavigationServer3D.FreeRid(map); }
}
