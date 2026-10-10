using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ProjectG.NavigationStudy;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: NavigationStudy.Bench <study-map.json> <report.json>");
    return 1;
}
var map = StudyMap.Read(args[0]);
var results = new List<object>();
foreach (var copies in new[] { 1, 32 })
{
    // Independent copies measure resident geometry/AOI query scaling, not a single huge connected route.
    var input = Repeat(map, copies);
    var baseline = GC.GetTotalMemory(true);
    var watch = Stopwatch.StartNew();
    var mesh = new StudyMesh(input);
    watch.Stop(); var meshMs = watch.Elapsed.TotalMilliseconds;
    var meshBytes = GC.GetTotalMemory(true) - baseline;
    watch.Restart(); var polygon = new PolygonStudy(mesh); watch.Stop();
    var polygonMs = watch.Elapsed.TotalMilliseconds;
    var polygonBytes = GC.GetTotalMemory(true) - baseline - meshBytes;
    watch.Restart(); var grid = new TiledGridStudy(mesh); watch.Stop();
    var gridMs = watch.Elapsed.TotalMilliseconds;
    var gridBytes = GC.GetTotalMemory(true) - baseline - meshBytes - polygonBytes;
    var measurements = new List<object>();
    var output = new List<Vector3>(4096);
    foreach (var scenario in map.Scenarios)
    {
        Measure("polygon", polygon.Find);
        Measure("grid", grid.Find);
        void Measure(string name, Func<Vector3, Vector3, string, List<Vector3>, int, SearchStatus> find)
        {
            SearchStatus status = default;
            for (var warm = 0; warm < 200; warm++) status = find(scenario.Start.Vector, scenario.Goal.Vector, input.Revision, output, 20_000);
            if ((status == SearchStatus.Success) != scenario.Reachable)
                throw new InvalidOperationException($"{name}/{scenario.Name}: expected reachable={scenario.Reachable}, got {status}");
            var routeLength = 0f;
            for (var i = 1; i < output.Count; i++) routeLength += Vector3.Distance(output[i - 1], output[i]);
            const int iterations = 2000;
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            for (var i = 0; i < iterations; i++) find(scenario.Start.Vector, scenario.Goal.Vector, input.Revision, output, 20_000);
            watch.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            measurements.Add(new { Backend = name, Scenario = scenario.Name, Status = status.ToString(),
                MicrosecondsPerQuery = watch.Elapsed.TotalMilliseconds * 1000 / iterations,
                BytesAllocatedPerQuery = (double)allocated / iterations, RouteLength = routeLength,
                Expanded = name == "polygon" ? polygon.Expanded : grid.Expanded });
        }
    }
    results.Add(new { Copies = copies, MeshBuildMs = meshMs, PolygonBuildMs = polygonMs, GridBuildMs = gridMs,
        MeshManagedBytes = meshBytes, PolygonManagedBytes = polygonBytes, GridManagedBytes = gridBytes,
        ExportJsonBytes = JsonSerializer.SerializeToUtf8Bytes(input, StudyMap.JsonOptions).Length,
        PolygonNodes = polygon.Graph.Positions.Length, PolygonDirectedLinks = polygon.Graph.LinkCount,
        GridNodes = grid.Graph.Positions.Length, GridDirectedLinks = grid.Graph.LinkCount,
        TileComponents = grid.TileGraph.Positions.Length,
        PolygonDiagnosticBinaryBytes = PackedGraphBytes(polygon.Graph),
        GridDiagnosticBinaryBytes = PackedGraphBytes(grid.Graph) + PackedGraphBytes(grid.TileGraph),
        Measurements = measurements });
    GC.KeepAlive(mesh); GC.KeepAlive(polygon); GC.KeepAlive(grid);
}
var report = new { Runtime = Environment.Version.ToString(), Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
    TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
    Revision = map.Revision, TimestampUtc = DateTime.UtcNow, CellSize = .5, TileCells = 16,
    Note = "Release microbenchmark, warmed sequential queries; 32 disconnected copies model resident AOI data. Managed memory excludes Godot/native/rendering. No live network or server capacity claim.",
    Results = results };
File.WriteAllText(args[1], JsonSerializer.Serialize(report, StudyMap.JsonOptions) + "\n");
Console.WriteLine($"NAVIGATION_BENCH_PASS: {args[1]}");
return 0;

static StudyMap Repeat(StudyMap source, int count)
{
    var vertices = new List<Point>(); var polygons = new List<int[]>(); var boxes = new List<StudyBox>();
    for (var copy = 0; copy < count; copy++)
    {
        var dx = (copy % 8) * 48f; var dz = (copy / 8) * 40f; var offset = vertices.Count;
        vertices.AddRange(source.Vertices.Select(p => new Point(p.X + dx, p.Y, p.Z + dz)));
        polygons.AddRange(source.Polygons.Select(p => p.Select(i => i + offset).ToArray()));
        boxes.AddRange(source.Occluders.Select(b => b with { Center = new(b.Center.X + dx, b.Center.Y, b.Center.Z + dz) }));
    }
    return new StudyMap { Revision = source.Revision, AgentHeight = source.AgentHeight, AgentRadius = source.AgentRadius,
        Vertices = vertices.ToArray(), Polygons = polygons.ToArray(), Occluders = boxes.ToArray(), Scenarios = source.Scenarios };
}

static long PackedGraphBytes(StudyGraph graph)
{
    // Identical uncompressed diagnostic layout for comparison; this is not a proposed live packet.
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(1); writer.Write(graph.Positions.Length); writer.Write(graph.LinkCount);
    for (var i = 0; i < graph.Positions.Length; i++)
    {
        WritePoint(graph.Positions[i]); writer.Write(graph.Links[i].Count);
        foreach (var edge in graph.Links[i]) { writer.Write(edge.To); WritePoint(edge.Portal); }
    }
    return stream.Length;
    void WritePoint(Vector3 p) { writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
}
