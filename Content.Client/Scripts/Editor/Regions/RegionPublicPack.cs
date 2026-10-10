using System.Text.Json;
using Content.Shared.Regions;
using Godot;
using ProjectG.Regions.Authoring;

namespace ProjectG.Regions.Export;

/// <summary>Builds and reopens the actual public region PCK; server bindings never enter it.</summary>
public static class RegionPublicPack
{
    public static void BuildAndVerify(RegionExportPackage package, string output)
    {
        var revision = RegionExportPackage.Fingerprint(package.Encode());
        var prefix = $"res://RegionExports/{revision}/";
        using var packer = new PckPacker();
        Check(packer.PckStart(output), "Creating public region PCK");
        var manifest = package.Regions.OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new { r.Id, Scene = prefix + r.Id + ".tscn", GeometryHash = r.Geometry.Hash.ToString("X16") }).ToArray();
        Check(packer.AddFileFromBuffer(prefix + "manifest.json", JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Revision = revision, Regions = manifest })), "Packing public manifest");
        foreach (var entry in package.Regions)
        {
            var authored = ResourceLoader.Load<PackedScene>(entry.ScenePath, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<RegionRoot>();
            var clean = new Node3D { Name = authored.Name };
            var temporary = output + "." + entry.Id + ".tscn";
            try
            {
                foreach (var child in authored.GetChildren())
                {
                    if (child.Name == "AuthoringAnchors" || child.Name == "Preview") continue;
                    ClearOwners(child);
                    authored.RemoveChild(child); clean.AddChild(child);
                    Flatten(child, clean);
                }
                using var packed = new PackedScene();
                Check(packed.Pack(clean), "Packing clean scene");
                Check(ResourceSaver.Save(packed, temporary, ResourceSaver.SaverFlags.BundleResources), "Saving clean scene");
                if (ResourceLoader.GetDependencies(temporary).Length != 0)
                    throw new InvalidDataException("Public region scene must be self-contained.");
                Check(packer.AddFileFromBuffer(prefix + entry.Id + ".tscn", File.ReadAllBytes(temporary)), "Packing clean scene file");
            }
            finally { clean.Free(); authored.Free(); if (File.Exists(temporary)) File.Delete(temporary); }
        }
        Check(packer.Flush(), "Finalizing public region PCK");
        if (!ProjectSettings.LoadResourcePack(output)) throw new InvalidDataException("Cannot reopen public PCK.");
        // Inspect resources loaded from the pack, not the authoring copies in memory.
        foreach (var entry in manifest)
        {
            var packed = ResourceLoader.Load<PackedScene>(entry.Scene, cacheMode: ResourceLoader.CacheMode.Ignore)
                ?? throw new InvalidDataException("Missing packed region.");
            var scene = packed.Instantiate();
            try { Inspect(scene); }
            finally { scene.Free(); }
        }
        GD.Print($"REGION_PUBLIC_PACK_OK: {manifest.Length} sanitized scenes, no scripts/markers/server bindings.");
    }

    private static void Flatten(Node node, Node owner)
    {
        if (node.Name == "ScaleReferences") { node.GetParent().RemoveChild(node); node.Free(); return; }
        if (node.GetScript().VariantType != Variant.Type.Nil)
            throw new InvalidDataException($"Unexpected script in public scene: {node.Name}. Define a public presentation contract first.");
        // Flatten inherited scene ownership so stripped nodes cannot be restored by an instance link.
        node.SceneFilePath = "";
        node.Owner = owner;
        foreach (var child in node.GetChildren()) Flatten(child, owner);
    }

    private static void ClearOwners(Node node)
    {
        foreach (var child in node.GetChildren()) ClearOwners(child);
        node.Owner = null;
    }

    private static void Inspect(Node node)
    {
        if (node is RegionMarker || node.GetScript().VariantType != Variant.Type.Nil ||
            node.Name == "AuthoringAnchors" || node.Name == "Preview" || node.Name == "ScaleReferences")
            throw new InvalidDataException("Authoring metadata leaked into public PCK.");
        foreach (var child in node.GetChildren()) Inspect(child);
    }

    private static void Check(Error result, string operation)
    {
        if (result != Error.Ok) throw new IOException($"{operation}: {result}");
    }
}
