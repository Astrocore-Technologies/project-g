using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectG.NavigationStudy;

// Experimental offline format. It is deliberately separate from live wire/save contracts.
public readonly record struct Point(float X, float Y, float Z)
{
    [JsonIgnore] public Vector3 Vector => new(X, Y, Z);
    public static Point From(Vector3 value) => new(value.X, value.Y, value.Z);
}

public sealed record StudyScenario(string Name, Point Start, Point Goal, bool Reachable);
public sealed record StudyBox(Point Center, Point Size, Point Rotation);

public sealed class StudyMap
{
    public int Version { get; set; } = 1;
    public string Revision { get; set; } = "";
    public float AgentRadius { get; set; } = .45f;
    public float AgentHeight { get; set; } = 1.8f;
    public Point[] Vertices { get; set; } = [];
    public int[][] Polygons { get; set; } = [];
    public StudyScenario[] Scenarios { get; set; } = [];
    public StudyBox[] Occluders { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static StudyMap Read(string path)
    {
        // Offline input is bounded too; reject oversized files before allocating JSON objects.
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Study export exceeds 32 MiB.");
        var map = JsonSerializer.Deserialize<StudyMap>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Missing study map.");
        map.Validate();
        return map;
    }

    public void Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Revision) || Revision.Length > 128 ||
            !float.IsFinite(AgentRadius) || AgentRadius <= 0 || AgentRadius > 10 ||
            !float.IsFinite(AgentHeight) || AgentHeight <= 0 || AgentHeight > 20 ||
            Vertices is null || Vertices.Length is < 3 or > 200_000 ||
            Polygons is null || Polygons.Length is < 1 or > 100_000 ||
            Scenarios is null || Scenarios.Length > 128 || Occluders is null || Occluders.Length > 4096)
            throw new InvalidDataException("Invalid study header or bounds.");
        foreach (var p in Vertices) ValidatePoint(p);
        foreach (var polygon in Polygons)
        {
            if (polygon is null || polygon.Length is < 3 or > 6 || polygon.Distinct().Count() != polygon.Length)
                throw new InvalidDataException("Invalid polygon.");
            foreach (var index in polygon)
                if ((uint)index >= (uint)Vertices.Length) throw new InvalidDataException("Invalid vertex reference.");
        }
        foreach (var scenario in Scenarios)
        {
            if (scenario is null || string.IsNullOrWhiteSpace(scenario.Name) || scenario.Name.Length > 64)
                throw new InvalidDataException("Invalid scenario.");
            ValidatePoint(scenario.Start);
            ValidatePoint(scenario.Goal);
        }
        foreach (var box in Occluders)
        {
            if (box is null) throw new InvalidDataException("Missing occluder.");
            ValidatePoint(box.Center); ValidatePoint(box.Size); ValidatePoint(box.Rotation);
            if (box.Size.X <= 0 || box.Size.Y <= 0 || box.Size.Z <= 0)
                throw new InvalidDataException("Invalid occluder size.");
        }
    }

    private static void ValidatePoint(Point point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z) ||
            MathF.Abs(point.X) > 4096 || MathF.Abs(point.Y) > 4096 || MathF.Abs(point.Z) > 4096)
            throw new InvalidDataException("Non-finite or out-of-bounds coordinate.");
    }
}
