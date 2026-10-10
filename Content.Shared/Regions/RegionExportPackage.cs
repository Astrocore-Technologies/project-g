using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared.Navigation;

namespace Content.Shared.Regions;

// Offline contracts only. Instances containing authoring metadata stay on the server/build machine.
public sealed record RegionExportAnchor(Guid Id, string Kind, float X, float Z, float Radius = 0);
public sealed record RegionExportEntry(string Id, string ScenePath, string GeometryFile,
    string? PlacementsFile, FlatRegionGeometry Geometry, RegionExportAnchor[] Anchors,
    SortedDictionary<string, Guid> Bindings);
public sealed record RegionExportPackage(int Version, RegionExportEntry[] Regions,
    SortedDictionary<string, string> Sources)
{
    public const int CurrentVersion = 1;
    public const int MaxBytes = 512 * 1024;
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    // Canonical order makes repeated exports independent of scene child order and locale.
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this with
    {
        Regions = Regions.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r with
        {
            Anchors = r.Anchors.OrderBy(a => a.Id).ToArray(),
            Bindings = new SortedDictionary<string, Guid>(r.Bindings, StringComparer.Ordinal)
        }).ToArray(),
        Sources = new SortedDictionary<string, string>(Sources, StringComparer.Ordinal)
    }, Json);

    public static RegionExportPackage Parse(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Region package exceeds the size budget.");
        var package = JsonSerializer.Deserialize<RegionExportPackage>(bytes, Json)
            ?? throw new InvalidDataException("Missing region package.");
        package.Validate();
        return package;
    }

    public void Validate()
    {
        if (Version != CurrentVersion || Regions is null || Regions.Length is < 1 or > 16 ||
            Sources is null || Sources.Count is < 1 or > 2048)
            throw new InvalidDataException("Unsupported or unbounded region package.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var region in Regions)
        {
            if (region is null || !Key(region.Id) || !ids.Add(region.Id) ||
                !RelativePath(region.GeometryFile) || !files.Add(region.GeometryFile) ||
                region.ScenePath is null || !region.ScenePath.StartsWith("res://Scenes/Regions/", StringComparison.Ordinal) ||
                !RelativePath(region.ScenePath[6..]) || !region.ScenePath.EndsWith(".tscn", StringComparison.Ordinal) ||
                region.Geometry is null || region.Anchors is null || region.Anchors.Length > 256 ||
                region.Bindings is null || region.Bindings.Count > 32)
                throw new InvalidDataException("Invalid region export identity, paths or bounds.");
            if (region.PlacementsFile is { } file && (!RelativePath(file) || !files.Add(file)))
                throw new InvalidDataException($"{region.Id}: invalid placement file.");
            if (!Sources.ContainsKey("Content.Client/" + region.ScenePath[6..]))
                throw new InvalidDataException($"{region.Id}: missing scene source fingerprint.");
            var grid = region.Geometry.CreateGrid();
            // A planned opening can make an anchor reachable, but cannot silently erase another obstacle.
            var openMap = grid.ToMessage();
            foreach (var cell in region.Geometry.OpeningCells) openMap.BlockedCells[cell] = 0;
            var openGrid = new NavigationGrid(openMap);
            var anchors = new Dictionary<Guid, RegionExportAnchor>();
            foreach (var anchor in region.Anchors)
            {
                if (anchor is null || anchor.Id == Guid.Empty || !anchors.TryAdd(anchor.Id, anchor) ||
                    anchor.Kind is not ("entry" or "gate" or "spawn" or "interaction" or "patch") ||
                    !float.IsFinite(anchor.X) || !float.IsFinite(anchor.Z) || !float.IsFinite(anchor.Radius) ||
                    (anchor.Kind == "gate" ? anchor.Radius is <= 0 or > 10 : anchor.Radius != 0))
                    throw new InvalidDataException($"{region.Id}: invalid or duplicate anchor UUID.");
                if (anchor.Kind != "patch" && !openGrid.IsWalkable(new(anchor.X, anchor.Z)))
                    throw new InvalidDataException($"{region.Id}/{anchor.Id}: anchor is not walkable.");
            }
            foreach (var (name, id) in region.Bindings)
                if (!Key(name) || !anchors.ContainsKey(id))
                    throw new InvalidDataException($"{region.Id}/{name}: binding references a missing UUID.");
            if (region.PlacementsFile is not null)
            {
                if (!region.Bindings.TryGetValue("Spawn", out var spawn) || anchors[spawn].Kind != "entry")
                    throw new InvalidDataException($"{region.Id}: missing entry binding Spawn.");
                var origin = new Vector2(anchors[spawn].X, anchors[spawn].Z);
                var pathfinder = new NavigationPathfinder(grid);
                var path = new List<Vector2>();
                foreach (var (name, id) in region.Bindings)
                    if (!pathfinder.TryFindPath(origin, new(anchors[id].X, anchors[id].Z), path))
                        throw new InvalidDataException($"{region.Id}/{name}: placement cannot be reached from Spawn.");
            }
        }
        foreach (var (path, hash) in Sources)
            if (!RelativePath(path) || hash is null || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("Invalid source fingerprint.");
        if (Encode().Length > MaxBytes) throw new InvalidDataException("Region package exceeds the size budget.");
    }

    public static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string SourceFingerprint(string path, byte[] bytes)
    {
        // Git may change text line endings on checkout; that does not change the authored content.
        if (Path.GetExtension(path) is ".cs" or ".tscn" or ".tres" or ".json" or ".gd" or ".gdshader")
        {
            var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n");
            bytes = Encoding.UTF8.GetBytes(text);
        }
        return Fingerprint(bytes);
    }
    public static bool RelativePath(string? value) => value is { Length: > 0 and <= 240 } &&
        !value.Contains('\\') && !value.Contains(':') && !value.StartsWith('/') &&
        value.Split('/').All(p => p.Length > 0 && p is not ("." or ".."));
    private static bool Key(string? value) => value is { Length: > 0 and <= 64 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
