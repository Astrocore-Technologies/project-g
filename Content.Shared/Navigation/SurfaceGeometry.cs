using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.Shared.Navigation;

public readonly record struct SurfaceVertex(float X, float Y, float Z)
{
    [JsonIgnore] public Vector3 Vector => new(X, Y, Z);
    public static SurfaceVertex From(Vector3 p) => new(p.X, p.Y, p.Z);
}

public sealed record SurfaceBox(SurfaceVertex Center, SurfaceVertex Size, SurfaceVertex Rotation);

/// <summary>Public, bounded offline geometry; no NPC definitions, secrets or mutable world rules.</summary>
public sealed class SurfaceGeometry
{
    // Approved static-region budget. Transfers still use bounded 900-byte chunks;
    // this does not enlarge movement packets or the snapshot datagram budget.
    public const int MaxVertices = 16_384;
    public const int MaxPolygons = 16_384;
    public const int MaxBytes = 1024 * 1024;
    public int Version { get; set; } = 1;
    public string Revision { get; set; } = "";
    public string ScenePath { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public float AgentRadius { get; set; } = .45f;
    public float AgentHeight { get; set; } = 1.8f;
    // V1 describes walkability only. V2 can explicitly treat authored terrain
    // faces as solid LOS geometry; floating test floors keep their old meaning.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WalkableFacesOcclude { get; set; }
    public SurfaceVertex[] Vertices { get; set; } = [];
    public int[][] Polygons { get; set; } = [];
    public SurfaceBox[] Occluders { get; set; } = [];
    // V3 adds exact nonwalkable cliff faces; they share the existing static budget
    // and never become movement polygons or reveal server-only content.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int[][]? OcclusionFaces { get; set; }
    [JsonIgnore] public ulong Hash => Convert.ToUInt64(Revision, 16);
    public static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };

    public void Validate()
    {
        if (Version is not (1 or 2 or 3) || Version == 1 && WalkableFacesOcclude || Version < 3 && OcclusionFaces is not null || Revision is not { Length: 16 } || !ulong.TryParse(Revision, System.Globalization.NumberStyles.HexNumber, null, out var hash) || hash == 0 ||
            ScenePath is null || !ScenePath.StartsWith("res://Scenes/Regions/", StringComparison.Ordinal) || !ScenePath.EndsWith(".tscn", StringComparison.Ordinal) || ScenePath.Contains("..") || ScenePath.Length > 128 ||
            SourceHash is not { Length: 64 } || SourceHash.Any(c => !Uri.IsHexDigit(c)) ||
            !float.IsFinite(AgentRadius) || AgentRadius is < .1f or > 2 || !float.IsFinite(AgentHeight) || AgentHeight is < .5f or > 4 ||
            Vertices is null || Vertices.Length is < 3 or > MaxVertices || Polygons is null || Polygons.Length is < 1 or > MaxPolygons ||
            Occluders is null || Occluders.Length > 256 || (long)Polygons.Length+(OcclusionFaces?.Length ?? 0)>MaxPolygons)
            throw new InvalidDataException("Invalid bounded surface geometry header.");
        foreach (var p in Vertices) ValidatePoint(p);
        foreach (var polygon in Polygons)
        {
            if (polygon is null || polygon.Length is < 3 or > 6 || polygon.Distinct().Count() != polygon.Length)
                throw new InvalidDataException("Invalid surface polygon.");
            foreach (var index in polygon)
                if ((uint)index >= (uint)Vertices.Length) throw new InvalidDataException("Invalid surface vertex reference.");
        }
        foreach (var box in Occluders)
        {
            if (box is null) throw new InvalidDataException("Missing surface occluder.");
            ValidatePoint(box.Center); ValidatePoint(box.Size); ValidatePoint(box.Rotation);
            if (box.Size.X <= 0 || box.Size.Y <= 0 || box.Size.Z <= 0) throw new InvalidDataException("Invalid occluder size.");
        }
        foreach (var face in OcclusionFaces ?? [])
        {
            if (face is null || face.Length!=3 || face.Distinct().Count()!=3 ||
                face.Any(i=>(uint)i >= (uint)Vertices.Length))
                throw new InvalidDataException("Invalid bounded occlusion triangle.");
            if (Vector3.Cross(Vertices[face[1]].Vector-Vertices[face[0]].Vector,
                Vertices[face[2]].Vector-Vertices[face[0]].Vector).LengthSquared()<1e-12f)
                throw new InvalidDataException("Degenerate occlusion triangle.");
        }
    }

    private string CalculateRevision()
    {
        var copy = new SurfaceGeometry { Revision = "0000000000000001", Version = Version, ScenePath = ScenePath, SourceHash = SourceHash,
            AgentRadius = AgentRadius, AgentHeight = AgentHeight, WalkableFacesOcclude = WalkableFacesOcclude,
            Vertices = Vertices, Polygons = Polygons, Occluders = Occluders, OcclusionFaces = OcclusionFaces };
        return Fingerprint(JsonSerializer.Serialize(copy, Json))[..16];
    }
    public void Seal() => Revision = CalculateRevision();
    public byte[] Encode() { Validate(); var bytes = JsonSerializer.SerializeToUtf8Bytes(this, Json); if (bytes.Length > MaxBytes) throw new InvalidDataException("Surface export exceeds budget."); return bytes; }
    public static SurfaceGeometry Decode(byte[] bytes)
    {
        if (bytes.Length is < 2 or > MaxBytes) throw new InvalidDataException("Surface export size invalid.");
        var geometry = JsonSerializer.Deserialize<SurfaceGeometry>(bytes, Json) ?? throw new InvalidDataException("Missing surface export.");
        geometry.Validate();
        if (geometry.Revision != geometry.CalculateRevision()) throw new InvalidDataException("Surface content hash mismatch.");
        return geometry;
    }
    public static string Fingerprint(string source) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source.Replace("\r\n", "\n"))));
    private static void ValidatePoint(SurfaceVertex p)
    {
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) || MathF.Abs(p.X) > 512 || MathF.Abs(p.Y) > 128 || MathF.Abs(p.Z) > 512)
            throw new InvalidDataException("Invalid surface coordinate.");
    }
}
