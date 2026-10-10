using System.Text.Json;
using Content.Shared.Regions;
using Godot;
using ProjectG.Regions.Authoring;

namespace ProjectG.Regions.Export;

/// <summary>Shared editor/headless pipeline. It reads scenes without executing their gameplay nodes.</summary>
public static class RegionPackageExporter
{
    public sealed record Definition(string Id, string ScenePath, string GeometryFile,
        string? PlacementsFile, SortedDictionary<string, Guid> Bindings);
    public sealed record Manifest(int Version, Definition[] Regions);

    public static RegionExportPackage Build()
    {
        var root = ProjectSettings.GlobalizePath("res://..");
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        AddSource("Content.Server/Data/region-authoring.json");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(Path.Combine(root, "Content.Server/Data/region-authoring.json")), RegionExportPackage.Json)
            ?? throw new InvalidDataException("Missing region authoring manifest.");
        if (manifest.Version != 1 || manifest.Regions is null || manifest.Regions.Length is < 1 or > 16)
            throw new InvalidDataException("Invalid region authoring manifest.");
        var regions = new List<RegionExportEntry>();
        foreach (var definition in manifest.Regions.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (!definition.ScenePath.StartsWith("res://Scenes/Regions/", StringComparison.Ordinal) ||
                !RegionExportPackage.RelativePath(definition.ScenePath[6..]))
                throw new InvalidDataException("Scene must be inside res://Scenes/Regions.");
            CaptureDependencies(definition.ScenePath);
            var packed = ResourceLoader.Load<PackedScene>(definition.ScenePath, cacheMode: ResourceLoader.CacheMode.Ignore)
                ?? throw new InvalidDataException($"Missing scene: {definition.ScenePath}");
            var scene = packed.Instantiate<RegionRoot>();
            try { regions.Add(ExportScene(scene, definition)); }
            finally { scene.Free(); }
        }
        // Include compiler inputs that can change the bake without changing scene text.
        foreach (var folder in new[] { "Content.Client/Scripts/Editor/Regions", "Content.Shared/Regions" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs").Order(StringComparer.Ordinal))
                AddSource(Path.GetRelativePath(root, file).Replace('\\', '/'));
        AddSource("Content.Client/Tools/Regions/ExportRegions.cs");
        AddSource("Content.Client/Tests/Regions/RegionExportSmoke.cs");
        var result = new RegionExportPackage(1, regions.ToArray(), sources);
        result.Validate();
        return result;

        void AddSource(string relative)
        {
            if (!RegionExportPackage.RelativePath(relative)) throw new InvalidDataException("Unsafe source path.");
            sources[relative] = RegionExportPackage.SourceFingerprint(relative, File.ReadAllBytes(Path.Combine(root, relative)));
        }
        void CaptureDependencies(string path)
        {
            if (!path.StartsWith("res://", StringComparison.Ordinal)) throw new InvalidDataException($"External scene dependency: {path}");
            var relative = "Content.Client/" + path[6..];
            if (sources.ContainsKey(relative)) return;
            AddSource(relative);
            foreach (var dependency in ResourceLoader.GetDependencies(path))
            {
                var dep = dependency.Split("::", StringSplitOptions.None)[^1];
                if (!dep.StartsWith("res://", StringComparison.Ordinal))
                    throw new InvalidDataException($"Unresolved scene dependency: {dependency}");
                CaptureDependencies(dep);
            }
        }
    }

    public static RegionExportEntry ExportScene(RegionRoot scene, Definition definition)
    {
        if (scene.RegionId != definition.Id) throw new InvalidDataException("Scene RegionId disagrees with authoring manifest.");
        var geometry = FlatGeometryBake.Bake(scene);
        var anchors = new List<RegionExportAnchor>();
        Visit(scene, Transform3D.Identity);
        return new(definition.Id, definition.ScenePath, definition.GeometryFile, definition.PlacementsFile,
            geometry, anchors.OrderBy(a => a.Id).ToArray(), definition.Bindings);

        void Visit(Node parent, Transform3D transform)
        {
            foreach (var child in parent.GetChildren())
            {
                var local = child is Node3D spatial ? transform * spatial.Transform : transform;
                if (child is RegionMarker marker)
                {
                    var kind = marker switch
                    {
                        EntryMarker => "entry", GateMarker => "gate", ActorSpawnMarker => "spawn",
                        InteractionMarker => "interaction", NavigationPatchMarker => "patch",
                        _ => throw new InvalidDataException("Unsupported marker type.")
                    };
                    // Metre coordinates have millimetre precision. Identity never depends on a NodePath.
                    float Quantize(float value) => MathF.Round(value * 1000, MidpointRounding.ToEven) / 1000f + 0f;
                    anchors.Add(new(Guid.Parse(marker.AuthoredObjectId), kind, Quantize(local.Origin.X),
                        Quantize(local.Origin.Z), marker is GateMarker gate ? Quantize(gate.Radius) : 0));
                }
                Visit(child, local);
            }
        }
    }

    public static void WriteCandidate(string path)
    {
        var repository = Path.GetFullPath(ProjectSettings.GlobalizePath("res://.."));
        var full = Path.GetFullPath(path);
        var staging = Path.Combine(repository, ".artifacts") + Path.DirectorySeparatorChar;
        if (!full.StartsWith(staging, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".candidate.json", StringComparison.Ordinal))
            throw new InvalidDataException("Export candidate must be a .candidate.json file inside .artifacts.");
        var package = Build();
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        RegionPublicPack.BuildAndVerify(package, full + ".pck");
        using var stream = new FileStream(full, FileMode.Create, System.IO.FileAccess.Write, FileShare.None);
        stream.Write(package.Encode()); stream.Flush(true);
        GD.Print($"REGION_CANDIDATE_OK: {package.Regions.Length} scenes, {RegionExportPackage.Fingerprint(package.Encode())}");
    }
}
