using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using ProjectG.NavigationStudy;
using NVector3 = System.Numerics.Vector3;

namespace ProjectG.NavigationResearch;

/// <summary>Editable research scene; no connection to live movement, saves or network authority.</summary>
public partial class NavigationStudy : Node3D
{
    [ExportGroup("Study clearance profile")]
    [Export(PropertyHint.Range, "0.15,2,0.15")] public float AgentRadius { get; set; } = .45f;
    [Export(PropertyHint.Range, "0.5,4,0.05")] public float AgentHeight { get; set; } = 1.8f;
    [Export(PropertyHint.Range, "0,0.5,0.05")] public float MaxClimb { get; set; } = .25f;
    [Export(PropertyHint.Range, "0,60,1")] public float MaxSlope { get; set; } = 45;
    private readonly List<(NVector3[] Path, MeshInstance3D Actor, Color Color)> _previews = [];
    private float _elapsed;
    private Rid _bakeMap;

    public override async void _Ready()
    {
        var exportArgument = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--navigation-study-export="));
        var captureArgument = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--navigation-study-preview="));
        var smoke = exportArgument is not null;
        try
        {
            GetNode<Camera3D>("Camera").LookAt(Vector3.Zero);
            // CSG collision generation needs a physics frame before parsing static geometry.
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var mesh = new NavigationMesh
            {
                AgentRadius = AgentRadius, AgentHeight = AgentHeight, AgentMaxClimb = MaxClimb, AgentMaxSlope = MaxSlope,
                CellSize = .15f, CellHeight = .05f, RegionMinSize = 0, RegionMergeSize = 0,
                FilterWalkableLowHeightSpans = true,
                GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
                GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.GroupsWithChildren,
                GeometrySourceGroupName = "navigation_study_geometry"
            };
            _bakeMap = NavigationServer3D.MapCreate();
            NavigationServer3D.MapSetCellSize(_bakeMap, mesh.CellSize);
            NavigationServer3D.MapSetCellHeight(_bakeMap, mesh.CellHeight);
            var region = new NavigationRegion3D { Enabled = false };
            region.SetNavigationMap(_bakeMap);
            AddChild(region);
            region.NavigationMesh = mesh;
            var watch = Stopwatch.StartNew();
            region.BakeNavigationMesh(false);
            watch.Stop();
            var map = Export(mesh);
            map.Revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(map, StudyMap.JsonOptions)))[..16];
            map.Validate();
            var studyMesh = new StudyMesh(map);
            var polygon = new PolygonStudy(studyMesh);
            var grid = new TiledGridStudy(studyMesh);
            var path = new List<NVector3>();
            var rows = new List<string> { $"Navigation study | bake {watch.Elapsed.TotalMilliseconds:F0} ms | polygons {map.Polygons.Length}",
                "Yellow: polygon routes | blue: tiled surface grid | moving spheres: offline path preview" };
            foreach (var scenario in map.Scenarios)
            {
                var p = polygon.Find(scenario.Start.Vector, scenario.Goal.Vector, map.Revision, path);
                if (!smoke && p == SearchStatus.Success) AddPreview(path, Colors.Gold);
                var g = grid.Find(scenario.Start.Vector, scenario.Goal.Vector, map.Revision, path);
                if (!smoke && g == SearchStatus.Success) AddPreview(path, Colors.DeepSkyBlue);
                rows.Add($"{scenario.Name}: polygon {p}, grid {g}");
                GD.Print($"NAVIGATION_STUDY {scenario.Name}: polygon={p}, grid={g}");
                if (scenario.Reachable != (p == SearchStatus.Success) || scenario.Reachable != (g == SearchStatus.Success))
                    throw new InvalidDataException($"Scenario {scenario.Name} failed: polygon={p}, grid={g}.");
            }
            var visibility = new StudyVisibility(map);
            if (visibility.Clear(new(6, 1, -6), new(6, 5, -6)))
                throw new InvalidDataException("Bridge deck must block a vertical attack ray.");
            if (!visibility.Clear(new(-10, 1.5f, 2), new(-10, 5.5f, -11)))
                throw new InvalidDataException("Open hill attack ray was unexpectedly blocked.");
            GetNode<Label>("UI/Status").Text = string.Join('\n', rows);
            if (captureArgument is not null)
            {
                // Capture only this scene's rendered viewport for artifact review, never the desktop.
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var destination = captureArgument["--navigation-study-preview=".Length..];
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(destination) != Error.Ok) throw new IOException("Could not save study preview.");
                GD.Print("NAVIGATION_STUDY_PREVIEW_PASS");
                GetTree().Quit();
            }
            if (exportArgument is not null)
            {
                var destination = exportArgument["--navigation-study-export=".Length..];
                File.WriteAllText(destination, JsonSerializer.Serialize(map, StudyMap.JsonOptions) + "\n");
                GD.Print($"NAVIGATION_STUDY_PASS revision={map.Revision} triangles={studyMesh.Triangles.Length} gridNodes={grid.Graph.Positions.Length} tileComponents={grid.TileGraph.Positions.Length} bakeMs={watch.Elapsed.TotalMilliseconds:F2}");
                GetTree().Quit();
            }
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetNode<Label>("UI/Status").Text = "Navigation study failed: " + error.Message;
            if (smoke || captureArgument is not null) GetTree().Quit(1);
        }
    }

    private StudyMap Export(NavigationMesh mesh)
    {
        var polygons = new int[mesh.GetPolygonCount()][];
        for (var i = 0; i < polygons.Length; i++) polygons[i] = mesh.GetPolygon(i);
        var scenarios = GetNode<Node3D>("Scenarios").GetChildren().Select(node => new StudyScenario(node.Name,
            Point.From(ToNumerics(node.GetNode<Marker3D>("Start").GlobalPosition)),
            Point.From(ToNumerics(node.GetNode<Marker3D>("Goal").GlobalPosition)),
            !node.HasMeta("reachable") || node.GetMeta("reachable").AsBool())).ToArray();
        var boxes = new List<StudyBox>();
        foreach (var body in GetNode<Node3D>("Geometry").GetChildren())
        {
            if (body is CsgBox3D csg)
            {
                if (!csg.GlobalBasis.Scale.IsEqualApprox(Vector3.One))
                    throw new InvalidDataException("Study visibility exporter supports unscaled CSG boxes only.");
                boxes.Add(new(Point.From(ToNumerics(csg.GlobalPosition)), Point.From(ToNumerics(csg.Size)), Point.From(ToNumerics(csg.GlobalRotation))));
            }
            else if (body is StaticBody3D spatial)
            {
                var shape = spatial.GetNode<CollisionShape3D>("Collision");
                if (shape.Shape is not BoxShape3D box || !shape.GlobalBasis.Scale.IsEqualApprox(Vector3.One))
                    throw new InvalidDataException("Study visibility exporter supports unscaled box colliders only.");
                boxes.Add(new(Point.From(ToNumerics(shape.GlobalPosition)), Point.From(ToNumerics(box.Size)), Point.From(ToNumerics(shape.GlobalRotation))));
            }
        }
        return new StudyMap { Revision = "pending", AgentRadius = AgentRadius, AgentHeight = AgentHeight,
            Vertices = mesh.GetVertices().Select(v => Point.From(ToNumerics(v))).ToArray(),
            Polygons = polygons, Scenarios = scenarios, Occluders = boxes.ToArray() };
    }

    private void AddPreview(List<NVector3> path, Color color)
    {
        var lines = new ImmediateMesh();
        var material = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        lines.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        for (var i = 1; i < path.Count; i++)
        {
            lines.SurfaceAddVertex(ToGodot(path[i - 1]) + Vector3.Up * .18f);
            lines.SurfaceAddVertex(ToGodot(path[i]) + Vector3.Up * .18f);
        }
        lines.SurfaceEnd();
        AddChild(new MeshInstance3D { Mesh = lines });
        var actor = new MeshInstance3D { Mesh = new SphereMesh { Radius = .25f, Height = .5f }, MaterialOverride = material };
        AddChild(actor);
        _previews.Add((path.ToArray(), actor, color));
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        foreach (var preview in _previews)
        {
            var length = 0f;
            for (var i = 1; i < preview.Path.Length; i++) length += NVector3.Distance(preview.Path[i - 1], preview.Path[i]);
            var travel = (_elapsed * 3) % (length + 1);
            for (var i = 1; i < preview.Path.Length; i++)
            {
                var span = NVector3.Distance(preview.Path[i - 1], preview.Path[i]);
                if (travel <= span || i == preview.Path.Length - 1)
                {
                    var point = NVector3.Lerp(preview.Path[i - 1], preview.Path[i], span > 0 ? Math.Clamp(travel / span, 0, 1) : 0);
                    preview.Actor.Position = ToGodot(point) + Vector3.Up * .45f; break;
                }
                travel -= span;
            }
        }
    }

    private static NVector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 ToGodot(NVector3 v) => new(v.X, v.Y, v.Z);

    public override void _ExitTree()
    {
        if (_bakeMap.IsValid) NavigationServer3D.FreeRid(_bakeMap);
    }
}
