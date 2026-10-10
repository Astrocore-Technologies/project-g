using System.Text.Json;
using Content.Shared.Navigation;

namespace Content.Server.Regions;

public static class SurfaceCatalog
{
    public const string PackagePath = "Data/Regions/surface-package.json";
    public static Dictionary<string, SurfaceGeometry> Load(string path, string? repository)
    {
        if (new FileInfo(path).Length > SurfaceGeometry.MaxBytes * 2) throw new InvalidDataException("Surface package exceeds budget.");
        var bytes = File.ReadAllBytes(path);
        var result = JsonSerializer.Deserialize<Dictionary<string, SurfaceGeometry>>(bytes, SurfaceGeometry.Json)
            ?? throw new InvalidDataException("Missing surface package.");
        if (result.Count is < 1 or > 2) throw new InvalidDataException("Invalid surface region count.");
        foreach (var (id, geometry) in result)
        {
            RegionOwnership.ValidateRegion(id); geometry.Validate(); _ = new SurfaceNavigation(geometry); _ = SurfaceGeometry.Decode(geometry.Encode());
            if (repository is null) continue;
            var source = Path.Combine(repository, "Content.Client", geometry.ScenePath[6..]);
            if (SurfaceGeometry.Fingerprint(File.ReadAllText(source)) != geometry.SourceHash)
                throw new InvalidDataException("Authored surface scene changed; automatic export is required.");
        }
        return result;
    }
}
