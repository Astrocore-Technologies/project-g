using System.Text.Json;
using Content.Shared.Navigation;

namespace Content.Server.Regions;

public static class SurfaceCatalog
{
    public const string PackagePath = "Data/Regions/surface-package.json";
    public static Dictionary<string, SurfaceGeometry> Load(string path, string? repository)
    {
        // Bound the package by the same regional budget as the world graph. Each
        // individual surface still retains its existing network transfer limit.
        if (new FileInfo(path).Length > SurfaceGeometry.MaxBytes * RegionalWorlds.MaxRegions) throw new InvalidDataException("Surface package exceeds budget.");
        var bytes = File.ReadAllBytes(path);
        var result = JsonSerializer.Deserialize<Dictionary<string, SurfaceGeometry>>(bytes, SurfaceGeometry.Json)
            ?? throw new InvalidDataException("Missing surface package.");
        if (result.Count is < 1 or > RegionalWorlds.MaxRegions) throw new InvalidDataException("Invalid surface region count.");
        foreach (var (id, geometry) in result)
        {
            RegionOwnership.ValidateRegion(id); geometry.Validate(); _ = new SurfaceNavigation(geometry); _ = SurfaceGeometry.Decode(geometry.Encode());
            if (repository is null) continue;
            if (ProjectG.Authoring.SurfaceSourceFingerprint.Compute(Path.Combine(repository,"Content.Client"),geometry.ScenePath) != geometry.SourceHash)
                throw new InvalidDataException("Authored surface scene changed; automatic export is required.");
        }
        return result;
    }
}
