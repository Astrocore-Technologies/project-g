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
            var selectedRegion=OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--region="))?[9..];
            if(selectedRegion is not null && selectedRegion is not ("linvel_beech" or "linvel_gardens" or "linvel_fox_hills"))
                throw new InvalidDataException("Unknown bounded export region.");
            AddChild(new Camera3D {Name="OfflineBakeCamera",Current=true});
            var exports = new Dictionary<string, SurfaceGeometry>();
            foreach (var (id, path) in new[] { ("linvel_beech", "res://Scenes/Regions/Linvel/BeechValley.tscn"),
                ("linvel_gardens", "res://Scenes/Regions/Linvel/GardenValleys.tscn"),
                ("linvel_fox_hills", "res://Scenes/Regions/Linvel/FoxHills.tscn") })
            {
                if(selectedRegion is not null && id!=selectedRegion) continue;
                var scene = GD.Load<PackedScene>(path).Instantiate<Node3D>(); AddChild(scene);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                PrepareTerrainInputs(scene);
                // Large outdoor scenes expose their bake resolution in editable scene metadata.
                var cellSize = scene.HasMeta("surface_cell_size") ? (float)scene.GetMeta("surface_cell_size") : .15f;
                var cellHeight = scene.HasMeta("surface_cell_height") ? (float)scene.GetMeta("surface_cell_height") : .05f;
                var mesh = new NavigationMesh { AgentRadius = .45f, AgentHeight = 1.8f, AgentMaxClimb = scene.HasMeta("surface_max_climb") ? (float)scene.GetMeta("surface_max_climb") : .25f,
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
                foreach (var node in scene.HasMeta("region_id")
                    ? SceneNodes(scene).Where(n => n.IsInGroup("surface_occluder"))
                    : scene.GetNode<Node3D>("Geometry").GetChildren())
                {
                    // Terrain LOS comes from the exported walkable surface; a giant box would hide the valley.
                    // Decorative/non-opaque blockers still participate in the navigation bake.
                    if (node.IsInGroup("surface_terrain") || scene.HasMeta("region_id") && !node.IsInGroup("surface_occluder")) continue;
                    // Dense authored woodland keeps precise trunk collision and
                    // uses the existing bounded triangle budget for LOS, rather
                    // than merging separated trees into an invisible large box.
                    if (node.IsInGroup("surface_exact_occluder")) continue;
                    if (node is CsgBox3D csg) boxes.Add(new(V(csg.GlobalPosition), V(csg.Size), V(csg.GlobalRotation)));
                    else if (node is StaticBody3D body)
                    {
                        var collider = body.GetNode<CollisionShape3D>("Collision");
                        if (collider.Shape is not BoxShape3D box)
                            throw new InvalidDataException("Surface exporter requires box collision geometry.");
                        // An editable asset root moves, rotates and uniformly scales both model and collider.
                        var scale = collider.GlobalBasis.Scale.Abs();
                        if (scale.X <= 0 || !Mathf.IsEqualApprox(scale.X, scale.Y) || !Mathf.IsEqualApprox(scale.X, scale.Z) || collider.GlobalBasis.Determinant() <= 0)
                            throw new InvalidDataException("Collider asset roots require positive uniform scale.");
                        var rotation = scale.IsEqualApprox(Vector3.One) ? collider.GlobalRotation : collider.GlobalBasis.Orthonormalized().GetEuler();
                        boxes.Add(new(V(collider.GlobalPosition), V(box.Size * scale), V(rotation)));
                    }
                    else throw new InvalidDataException("Unsupported surface collider.");
                }
                // Steep faces are removed from the walkable bake. Native solid volumes
                // retain their LOS while staying separate from movement and Jolt collision.
                if (scene.HasMeta("surface_solid_terrain_los") && scene.GetMeta("surface_solid_terrain_los").AsBool())
                    foreach (var terrain in scene.GetNode<Node3D>("Geometry").GetChildren().Where(n=>n.GetClass()=="Terrain3D"))
                        boxes.AddRange(TerrainVisibilityExport.Bake(terrain,256-boxes.Count,
                            scene.HasMeta("surface_steep_only_terrain_los") && scene.GetMeta("surface_steep_only_terrain_los").AsBool()));
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
                    if (scene.HasMeta("surface_snap_native_heights") && scene.GetMeta("surface_snap_native_heights").AsBool())
                        SnapNativeHeights(scene, vertices);
                    if (scene.HasMeta("surface_road_samples"))
                        RefineNativeRoads(scene, ref vertices, ref polygons);
                    boxes = boxes.Select(b=>new SurfaceBox(Round(b.Center,3),Round(b.Size,3),Round(b.Rotation,6))).ToList();
                    polygons = ProjectG.NavigationStudy.SurfaceExportPacking.PairTriangles(polygons);
                    for (var i = 0; i < vertices.Length; i++) vertices[i] = new((float)Math.Round(vertices[i].X,3),(float)Math.Round(vertices[i].Y,3),(float)Math.Round(vertices[i].Z,3));
                }
                var exactCliffs = scene.HasMeta("surface_native_cliff_faces") && scene.GetMeta("surface_native_cliff_faces").AsBool();
                var cliffFaces = exactCliffs ? AddNativeCliffFaces(scene,ref vertices,polygons.Length) : null;
                var geometry = new SurfaceGeometry { ScenePath = path, SourceHash = ProjectG.Authoring.SurfaceSourceFingerprint.Compute(ProjectSettings.GlobalizePath("res://"),path),
                    Version = exactCliffs ? 3 : scene.HasMeta("region_id") ? 2 : 1, WalkableFacesOcclude = scene.HasMeta("region_id"), Vertices = vertices, Polygons = polygons, Occluders = boxes.ToArray(), OcclusionFaces=cliffFaces };
                GD.Print($"SURFACE_COUNTS {id} vertices={geometry.Vertices.Length} polygons={polygons.Length} cliff_faces={cliffFaces?.Length ?? 0} boxes={boxes.Count}");
                geometry.Seal(); geometry.Validate();
                try { _ = new SurfaceNavigation(geometry); }
                catch (InvalidDataException)
                {
                    // Preserve rejected offline input for diagnosis, never publish it.
                    File.WriteAllBytes(output + ".rejected.json", geometry.Encode());
                    throw;
                }
                _ = geometry.Encode();
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
    private static int[][] AddNativeCliffFaces(Node3D scene, ref SurfaceVertex[] vertices, int walkableCount)
    {
        var points=vertices.ToList();
        var indices=new Dictionary<SurfaceVertex,int>();
        for (var i=0;i<points.Count;i++) indices.TryAdd(points[i],i);
        var triangles=new List<int[]>();
        foreach (var terrain in scene.GetNode<Node3D>("Geometry").GetChildren().OfType<Node3D>().Where(n=>n.GetClass()=="Terrain3D"))
        {
            var native=terrain.Call("bake_mesh",0,0).AsGodotObject() as Mesh
                ?? throw new InvalidDataException("Missing native cliff visibility mesh.");
            var faces=native.GetFaces();
            for (var i=0;i+2<faces.Length;i+=3)
            {
                var a=terrain.GlobalTransform*faces[i];var b=terrain.GlobalTransform*faces[i+1];var c=terrain.GlobalTransform*faces[i+2];
                var normal=(b-a).Cross(c-a);
                if (normal.LengthSquared()<1e-10f || MathF.Abs(normal.Normalized().Y)>=MathF.Cos(MathF.PI/4)) continue;
                if (walkableCount+triangles.Count>=SurfaceGeometry.MaxPolygons)
                    throw new InvalidDataException("Native cliffs exceed the shared static face budget.");
                triangles.Add([Index(a),Index(b),Index(c)]);
            }
        }
        // Exact, closed faces of explicitly marked physical trunks. These
        // share the same vertex/face/byte caps as native cliffs; no extra wire
        // fields or unbounded collider list is introduced.
        int[][] boxFaces = [[0,2,1],[0,3,2],[4,5,6],[4,6,7],
            [0,1,5],[0,5,4],[1,2,6],[1,6,5],[2,3,7],[2,7,6],[3,0,4],[3,4,7]];
        foreach (var body in SceneNodes(scene).OfType<StaticBody3D>().Where(n=>n.IsInGroup("surface_exact_occluder")))
        {
            var collision=body.GetNode<CollisionShape3D>("Collision");
            if (collision.Shape is not BoxShape3D box)
                throw new InvalidDataException("Exact woodland occlusion requires an authored box collider.");
            var scale=collision.GlobalBasis.Scale.Abs();
            if (scale.X<=0 || !Mathf.IsEqualApprox(scale.X,scale.Y) || !Mathf.IsEqualApprox(scale.X,scale.Z) || collision.GlobalBasis.Determinant()<=0)
                throw new InvalidDataException("Exact woodland roots require positive uniform scale.");
            var h=box.Size*.5f;
            Vector3[] corners=[new(-h.X,-h.Y,-h.Z),new(h.X,-h.Y,-h.Z),new(h.X,h.Y,-h.Z),new(-h.X,h.Y,-h.Z),
                new(-h.X,-h.Y,h.Z),new(h.X,-h.Y,h.Z),new(h.X,h.Y,h.Z),new(-h.X,h.Y,h.Z)];
            var ids=corners.Select(p=>Index(collision.GlobalTransform*p)).ToArray();
            foreach(var face in boxFaces)
            {
                if (walkableCount+triangles.Count>=SurfaceGeometry.MaxPolygons)
                    throw new InvalidDataException("Authored woodland exceeds the shared static face budget.");
                triangles.Add([ids[face[0]],ids[face[1]],ids[face[2]]]);
            }
        }
        vertices=points.ToArray();
        return triangles.ToArray();

        int Index(Vector3 value)
        {
            var point=Round(V(value),3);
            if (indices.TryGetValue(point,out var index)) return index;
            if (points.Count>=SurfaceGeometry.MaxVertices) throw new InvalidDataException("Native cliffs exceed the static vertex budget.");
            indices.Add(point,points.Count);points.Add(point);return points.Count-1;
        }
    }
    private static void PrepareTerrainInputs(Node3D scene)
    {
        // GroupsWithChildren must traverse each collider once. Authored roots
        // and their child bodies can both carry the group for editor tools.
        // Normalize only this temporary bake instance, leaving the saved scene intact.
        scene.AddToGroup("surface_geometry");
        foreach (var descendant in SceneNodes(scene).Where(n => n != scene))
            descendant.RemoveFromGroup("surface_geometry");
        var geometry=scene.GetNode<Node3D>("Geometry");
        foreach(var terrain in geometry.GetChildren().Where(n=>n.GetClass()=="Terrain3D").ToArray())
        {
            if(terrain is not Node3D spatial || !spatial.GlobalTransform.IsEqualApprox(Transform3D.Identity))
                throw new InvalidDataException("Terrain3D must use an identity world transform.");
            // Curved ramps need the authored heightfield resolution. Other
            // regions retain their existing coarse offline bake by default.
            var terrainLod = scene.HasMeta("surface_terrain_lod") ? (int)scene.GetMeta("surface_terrain_lod") : 1;
            var mesh=terrain.Call("bake_mesh",terrainLod,0).AsGodotObject() as Mesh
                ?? throw new InvalidDataException("Terrain3D returned no authored terrain mesh.");
            var faces=mesh.GetFaces();
            if(faces.Length==0) throw new InvalidDataException("Terrain3D has no walkable authored data.");
            terrain.Set("collision_mode",0);
            var body=new StaticBody3D {Name="OfflineTerrainBake",CollisionLayer=1};
            body.AddToGroup("surface_terrain"); geometry.AddChild(body);
            body.AddChild(new CollisionShape3D {Name="Collision",Shape=new ConcavePolygonShape3D {Data=faces}});
        }
    }
    // Region blockers can live inside editable object roots anywhere in the scene.
    // The traversal is offline only; legacy flat scenes retain their export order.
    private static IEnumerable<Node> SceneNodes(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            yield return child;
            foreach (var descendant in SceneNodes(child)) yield return descendant;
        }
    }
    private static SurfaceVertex V(Vector3 v) => new(v.X, v.Y, v.Z);
    // Recast quantizes floor heights and sparsely samples large polygons. Keep
    // its obstacle/portal topology, but restore vertex heights from editable
    // native ground or the nearest real walkable deck. No runtime projection.
    private static void SnapNativeHeights(Node3D scene, SurfaceVertex[] vertices)
    {
        var terrain = scene.GetNode<Node3D>("Geometry").GetChildren().Single(n=>n.GetClass()=="Terrain3D");
        var data = terrain.Get("data").AsGodotObject();
        var decks = SceneNodes(scene).OfType<StaticBody3D>()
            .Where(b=>b.CollisionLayer==1 && !b.IsInGroup("surface_terrain"))
            .Select(b=>b.GetNodeOrNull<CollisionShape3D>("Collision"))
            .Where(c=>c?.Shape is BoxShape3D).Select(c=>c!).ToArray();
        for (var i=0;i<vertices.Length;i++)
        {
            var v=vertices[i];
            var height=data.Call("get_height",new Vector3(v.X,0,v.Z)).AsSingle();
            var error=float.IsFinite(height)?MathF.Abs(height-v.Y):float.PositiveInfinity;
            foreach (var deck in decks)
            {
                var half=((BoxShape3D)deck.Shape).Size*.5f;
                var local=deck.GlobalTransform.AffineInverse()*new Vector3(v.X,v.Y,v.Z);
                if(MathF.Abs(local.X)>half.X+.1f || MathF.Abs(local.Z)>half.Z+.1f)continue;
                var top=(deck.GlobalTransform*new Vector3(local.X,half.Y,local.Z)).Y;
                if(MathF.Abs(top-v.Y)<error) {height=top;error=MathF.Abs(top-v.Y);}
            }
            if(error>3 || !float.IsFinite(height))
                throw new InvalidDataException($"Cannot reconcile authored floor at {v}.");
            vertices[i]=new(v.X,height,v.Z);
        }
    }

    // Local, conforming subdivision retains Recast's obstacle/portal topology.
    // Both owners of an edge are split together, so precision never creates a
    // T-junction or a crack. Native Terrain3D remains the editable height source.
    private static void RefineNativeRoads(Node3D scene, ref SurfaceVertex[] sourceVertices, ref int[][] sourcePolygons)
    {
        // Saved public geometry samples are part of the scene fingerprint/PCK,
        // with no runtime dependency on a loose authoring JSON file.
        var tolerance=scene.HasMeta("surface_road_tolerance")?scene.GetMeta("surface_road_tolerance").AsSingle():.18f;
        if(!float.IsFinite(tolerance) || tolerance is < .15f or > .45f) throw new InvalidDataException("Invalid road precision tolerance.");
        var samples = scene.GetMeta("surface_road_samples").AsVector3Array()
            .Select(p => (Point:new System.Numerics.Vector3(p.X,p.Y,p.Z),Tolerance:tolerance,Required:true)).ToList();
        var roadCount=samples.Count;
        if(scene.HasMeta("surface_global_sample_spacing"))
        {
            var spacing=scene.GetMeta("surface_global_sample_spacing").AsSingle();
            if(!float.IsFinite(spacing) || spacing is < 16 or > 64) throw new InvalidDataException("Invalid terrain sampling budget.");
            var data=scene.GetNode<Node3D>("Geometry").GetChildren().Single(n=>n.GetClass()=="Terrain3D").Get("data").AsGodotObject();
            // Also check the freely explored forest floor, excluding missing
            // steep/blocked floors. Roads retain their tighter required tolerance.
            for(var z=-496f;z<=496;z+=spacing)
            for(var x=-496f;x<=496;x+=spacing)
            {
                var height=data.Call("get_height",new Vector3(x,0,z)).AsSingle();
                if(float.IsFinite(height)) samples.Add((new(x,height,z),.45f,false));
            }
        }
        if(samples.Count is < 2 or > 8192) throw new InvalidDataException("Invalid bounded terrain profile.");
        var vertices = new List<SurfaceVertex>();
        var welded = new Dictionary<SurfaceVertex,int>();
        var remap = new int[sourceVertices.Length];
        for(var i=0;i<sourceVertices.Length;i++)
        {
            var v=Round(sourceVertices[i],3);
            if(!welded.TryGetValue(v,out var index)) { index=vertices.Count;welded.Add(v,index);vertices.Add(v); }
            remap[i]=index;
        }
        var triangles = sourcePolygons.SelectMany(p => Enumerable.Range(1,p.Length-2)
            .Select(i=>new[]{remap[p[0]],remap[p[i]],remap[p[i+1]]})).ToList();
        for(var pass=0;pass<24;pass++)
        {
            var map=new SurfaceGeometry { Revision="0000000000000001", ScenePath="res://Scenes/Regions/Offline.tscn", SourceHash=new string('A',64),
                Vertices=vertices.ToArray(),Polygons=triangles.ToArray() };
            var navigation=new SurfaceNavigation(map);
            var selected=new HashSet<(int A,int B)>();
            foreach(var sample in samples)
            {
                var expected=sample.Point;
                if(navigation.TryLocate(expected,sample.Tolerance,out _)) continue;
                // A missing/blocked floor cannot be repaired by moving its height.
                if(!navigation.TryLocate(expected,1f,out var located))
                {
                    var column=new List<SurfaceLocation>();navigation.SampleColumn(expected.X,expected.Z,column);
                    if(column.Count==0) { if(!sample.Required) continue; throw new InvalidDataException($"Authored road has no floor at {expected}."); }
                    located=column.MinBy(p=>MathF.Abs(p.Position.Y-expected.Y));
                    if(MathF.Abs(located.Position.Y-expected.Y)>3) { if(!sample.Required) continue; throw new InvalidDataException($"Authored road uses another floor at {expected}."); }
                }
                var t=triangles[located.Triangle];
                var edge=Enumerable.Range(0,3).Select(j=>(A:t[j],B:t[(j+1)%3]))
                    .MaxBy(e=>System.Numerics.Vector3.DistanceSquared(vertices[e.A].Vector,vertices[e.B].Vector));
                selected.Add(edge.A<edge.B?edge:(edge.B,edge.A));
            }
            if(selected.Count==0)
            {
                sourceVertices=vertices.ToArray();sourcePolygons=triangles.ToArray();
                GD.Print($"ROAD_NATIVE_PRECISION samples={roadCount} terrain_samples={samples.Count-roadCount} passes={pass} vertices={vertices.Count}");return;
            }
            foreach(var edge in selected.OrderBy(e=>e.A).ThenBy(e=>e.B))
            {
                var midpoint=SurfaceVertex.From((vertices[edge.A].Vector+vertices[edge.B].Vector)*.5f);
                var native=new[]{midpoint};SnapNativeHeights(scene,native);midpoint=Round(native[0],3);
                if(welded.ContainsKey(midpoint)) continue;
                if(vertices.Count>=SurfaceGeometry.MaxVertices) throw new InvalidDataException("Road precision exceeds the approved vertex budget.");
                var index=vertices.Count;vertices.Add(midpoint);welded.Add(midpoint,index);
                for(var i=triangles.Count-1;i>=0;i--)
                {
                    var t=triangles[i];
                    if(!t.Contains(edge.A)||!t.Contains(edge.B))continue;
                    var c=t.Single(v=>v!=edge.A&&v!=edge.B);
                    triangles[i]=[edge.A,index,c];triangles.Add([index,edge.B,c]);
                }
                if(triangles.Count>SurfaceGeometry.MaxPolygons) throw new InvalidDataException("Road precision exceeds the bounded polygon budget.");
            }
        }
        var lastMap=new SurfaceGeometry { Revision="0000000000000001", ScenePath="res://Scenes/Regions/Offline.tscn", SourceHash=new string('A',64),Vertices=vertices.ToArray(),Polygons=triangles.ToArray() };
        var lastNavigation=new SurfaceNavigation(lastMap);
        var lastData=scene.GetNode<Node3D>("Geometry").GetChildren().Single(n=>n.GetClass()=="Terrain3D").Get("data").AsGodotObject();
        foreach(var sample in samples.Where(p=>p.Required&&!lastNavigation.TryLocate(p.Point,p.Tolerance,out _)).Take(12))
            GD.Print($"ROAD_NATIVE_UNRESOLVED expected={sample.Point} native={lastData.Call("get_height",new Vector3(sample.Point.X,0,sample.Point.Z)).AsSingle()}");
        throw new InvalidDataException("Authored road precision did not converge.");
    }
    // Same millimetre precision as terrain vertices; avoid unbounded float tails.
    private static SurfaceVertex Round(SurfaceVertex v,int digits) => new((float)Math.Round(v.X,digits),(float)Math.Round(v.Y,digits),(float)Math.Round(v.Z,digits));

    private static int[][] RemoveBlockerTops(Node3D scene,SurfaceVertex[] vertices,int[][] polygons,float cellSize,float cellHeight)
    {
        var caps = new List<(Transform3D Inverse,Vector3 Half)>();
        foreach (var node in SceneNodes(scene))
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
