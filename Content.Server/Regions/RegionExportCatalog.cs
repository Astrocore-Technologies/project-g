using Content.Shared.Regions;

namespace Content.Server.Regions;

/// <summary>Reads one immutable export snapshot before simulation or persistence starts.</summary>
public sealed class RegionExportCatalog
{
    public RegionExportPackage Package { get; }

    public RegionExportCatalog(string path, string? sourceRoot = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > RegionExportPackage.MaxBytes)
            throw new InvalidDataException("Region package is missing or oversized. Run tools/Export-Regions.ps1.");
        Package = RegionExportPackage.Parse(File.ReadAllBytes(path));
        if (sourceRoot is not null) VerifySources(sourceRoot);
    }

    public void VerifySources(string root)
    {
        foreach (var (relative, expected) in Package.Sources)
        {
            var path = Path.Combine(root, relative);
            if (!File.Exists(path) || RegionExportPackage.SourceFingerprint(relative, File.ReadAllBytes(path)) != expected)
                throw new InvalidDataException($"Stale region export: {relative}. Run tools/Export-Regions.ps1.");
        }
    }

    public RegionExportEntry Require(string region, string geometryFile, string? placementsFile)
    {
        var entry = Package.Regions.SingleOrDefault(r => r.Id == region);
        if (entry is null || entry.GeometryFile != geometryFile || entry.PlacementsFile != placementsFile)
            throw new InvalidDataException($"{region}: region rules do not match the published export.");
        return entry;
    }

    public static Dictionary<string, float[]> Placements(RegionExportEntry entry)
    {
        var anchors = entry.Anchors.ToDictionary(a => a.Id);
        return entry.Bindings.ToDictionary(b => b.Key, b => new[] { anchors[b.Value].X, anchors[b.Value].Z }, StringComparer.Ordinal);
    }

    public static void RequireRouteAnchor(RegionExportEntry entry, string name, string kind, float? radius = null)
    {
        if (!entry.Bindings.TryGetValue(name, out var id))
            throw new InvalidDataException($"{entry.Id}/{name}: route references a missing binding.");
        var anchor = entry.Anchors.Single(a => a.Id == id);
        if (anchor.Kind != kind || radius is { } r && MathF.Abs(r - anchor.Radius) > .0001f)
            throw new InvalidDataException($"{entry.Id}/{name}: route kind or radius does not match the authored marker.");
    }

    // Source check is a development safeguard; deployed servers need only the validated package.
    public static string? FindSourceRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Game.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, "Content.Client", "project.godot"))) return directory.FullName;
        return null;
    }
}
