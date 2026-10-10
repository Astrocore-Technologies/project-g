using System.Text;
using Content.Server.Regions;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Content.Shared.Regions;
using Xunit;

namespace Content.Tests.Server;

public sealed class RegionExportTests
{
    private static readonly Guid Entry = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Gate = Guid.Parse("10000000-0000-4000-8000-000000000002");

    private static RegionExportPackage Sample()
    {
        var geometry = FlatRegionGeometry.FromGrid(new NavigationGrid(new RegionNavigation(new(0, 0), 1, .45f, 5, 5, new byte[25])));
        return new(1, [new("test", "res://Scenes/Regions/test.tscn", "Data/Regions/test.json", "Data/Regions/points.json", geometry,
            [new(Entry, "entry", 1.5f, 1.5f), new(Gate, "gate", 3.5f, 3.5f, 1)],
            new(StringComparer.Ordinal) { ["Spawn"] = Entry, ["Gate"] = Gate })],
            new(StringComparer.Ordinal) { ["Content.Client/Scenes/Regions/test.tscn"] = RegionExportPackage.Fingerprint("scene"u8.ToArray()) });
    }

    [Fact]
    public void RoundTripAndCanonicalOrderPreserveIdentity()
    {
        var package = Sample();
        var entry = package.Regions[0];
        var reordered = package with { Regions = [entry with { Anchors = entry.Anchors.Reverse().ToArray() }] };
        Assert.Equal(package.Encode(), reordered.Encode());
        var decoded = RegionExportPackage.Parse(package.Encode());
        Assert.Equal(Entry, decoded.Regions[0].Bindings["Spawn"]);
        Assert.Equal(package.Regions[0].Geometry.Hash, decoded.Regions[0].Geometry.Hash);
    }

    [Fact]
    public void RejectsDuplicateIdsMissingBindingsAndBlockedEntries()
    {
        var package = Sample(); var entry = package.Regions[0];
        Assert.Throws<InvalidDataException>(() => (package with { Regions = [entry with { Anchors = [entry.Anchors[0], entry.Anchors[0]] }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (package with { Regions = [entry with { Bindings = new() { ["Spawn"] = Guid.NewGuid() } }] }).Validate());
        var map = entry.Geometry.CreateGrid().ToMessage(); map.BlockedCells[6] = 1;
        Assert.Throws<InvalidDataException>(() => (package with { Regions = [entry with { Geometry = FlatRegionGeometry.FromGrid(new(map)) }] }).Validate());
    }

    [Fact]
    public void RejectsUnreachablePlacementAndIncorrectRouteTypes()
    {
        var package = Sample(); var entry = package.Regions[0];
        var map = entry.Geometry.CreateGrid().ToMessage();
        for (var z = 0; z < 5; z++) map.BlockedCells[z * 5 + 2] = 1;
        Assert.Throws<InvalidDataException>(() => (package with { Regions = [entry with { Geometry = FlatRegionGeometry.FromGrid(new(map)) }] }).Validate());
        RegionExportCatalog.RequireRouteAnchor(entry, "Gate", "gate", 1);
        Assert.Throws<InvalidDataException>(() => RegionExportCatalog.RequireRouteAnchor(entry, "Gate", "entry"));
        Assert.Throws<InvalidDataException>(() => RegionExportCatalog.RequireRouteAnchor(entry, "Gate", "gate", 2));
    }

    [Theory]
    [InlineData("../secrets.json")]
    [InlineData("C:/secrets.json")]
    [InlineData("/secrets.json")]
    [InlineData("Data\\secrets.json")]
    public void RejectsUnsafePaths(string path)
    {
        var package = Sample();
        Assert.Throws<InvalidDataException>(() => (package with { Regions = [package.Regions[0] with { GeometryFile = path }] }).Validate());
    }

    [Fact]
    public void RejectsOversizedAndUnknownSchema()
    {
        Assert.Throws<InvalidDataException>(() => RegionExportPackage.Parse(new byte[RegionExportPackage.MaxBytes + 1]));
        Assert.Throws<InvalidDataException>(() => (Sample() with { Version = 2 }).Validate());
        var json = Encoding.UTF8.GetString(Sample().Encode()).Replace("\"Version\": 1", "\"Unexpected\": 1, \"Version\": 1");
        Assert.Throws<System.Text.Json.JsonException>(() => RegionExportPackage.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void SourceFingerprintIgnoresGitTextLineEndings()
    {
        Assert.Equal(RegionExportPackage.SourceFingerprint("map.tscn", "a\nb\n"u8.ToArray()),
            RegionExportPackage.SourceFingerprint("map.tscn", "\uFEFFa\r\nb\r\n"u8.ToArray()));
    }

    [Fact]
    public void SourceChangesFailBeforeSimulationAndReadSnapshotRemainsPinned()
    {
        var root = Path.Combine(Path.GetTempPath(), "project-g-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Content.Client/Scenes/Regions"));
        try
        {
            var scene = Path.Combine(root, "Content.Client/Scenes/Regions/test.tscn");
            File.WriteAllText(scene, "scene");
            var path = Path.Combine(root, "package.json"); File.WriteAllBytes(path, Sample().Encode());
            var catalog = new RegionExportCatalog(path, root);
            File.WriteAllText(path, "broken");
            Assert.Equal(Entry, catalog.Require("test", "Data/Regions/test.json", "Data/Regions/points.json").Bindings["Spawn"]);
            File.WriteAllText(scene, "changed scene");
            Assert.Throws<InvalidDataException>(() => catalog.VerifySources(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
