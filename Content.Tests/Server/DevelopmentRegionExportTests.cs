using Content.Server.Regions;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Content.Shared.Regions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Content.Tests.Server;

public sealed class DevelopmentRegionExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "project-g-startup-" + Guid.NewGuid().ToString("N"));
    private string Application => Path.Combine(_root, "Content.Server/bin/Debug/net10.0");
    private string Published => Path.Combine(_root, "Content.Server", DevelopmentRegionExport.PackagePath);
    private const string Scene = "Content.Client/Scenes/Regions/test.tscn";

    public DevelopmentRegionExportTests()
    {
        Directory.CreateDirectory(Application);
        Directory.CreateDirectory(Path.GetDirectoryName(Published)!);
        Directory.CreateDirectory(Path.Combine(_root, "Content.Client/Scenes/Regions"));
        File.WriteAllText(Path.Combine(_root, "Game.slnx"), "");
        File.WriteAllText(Path.Combine(_root, "Content.Client/project.godot"), "");
        File.WriteAllText(Path.Combine(_root, Scene), "initial scene");
        Publish();
    }

    private IConfiguration Configuration(bool enabled = true, string package = DevelopmentRegionExport.PackagePath) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RegionExports:PackagePath"] = package, ["RegionExports:AutoExport"] = enabled.ToString()
        }).Build();

    private void Publish()
    {
        var geometry = FlatRegionGeometry.FromGrid(new NavigationGrid(new RegionNavigation(new(0, 0), 1, .45f, 5, 5, new byte[25])));
        var package = new RegionExportPackage(1,
            [new("test", "res://Scenes/Regions/test.tscn", "Data/Regions/test.json", null, geometry, [], new())],
            new() { [Scene] = RegionExportPackage.SourceFingerprint(Scene, File.ReadAllBytes(Path.Combine(_root, Scene))) });
        File.WriteAllBytes(Published, package.Encode());
    }

    [Fact]
    public async Task CurrentSourcesSkipExporterAndUsePublishedPackageInsteadOfStaleBuildCopy()
    {
        var configuration = Configuration();
        await DevelopmentRegionExport.EnsureCurrentAsync(configuration, Application,
            _ => throw new InvalidOperationException("Unchanged maps must not launch the exporter."), TextWriter.Null);
        Assert.Equal(Published, configuration["RegionExports:PackagePath"]);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task ChangedMissingOrCorruptPackageExportsBeforeContinuing(string change)
    {
        if (change == "changed") File.AppendAllText(Path.Combine(_root, Scene), " edited");
        if (change == "missing") File.Delete(Published);
        if (change == "corrupt") File.WriteAllText(Published, "broken");
        var calls = 0;
        var configuration = Configuration();
        await DevelopmentRegionExport.EnsureCurrentAsync(configuration, Application, root =>
        {
            Assert.Equal(_root, root); calls++; Publish(); return Task.CompletedTask;
        }, TextWriter.Null);
        Assert.Equal(1, calls);
        _ = new RegionExportCatalog(configuration["RegionExports:PackagePath"]!, _root);
    }

    [Fact]
    public async Task FailedExportStopsStartupAndKeepsPreviousPackage()
    {
        var before = File.ReadAllBytes(Published);
        File.AppendAllText(Path.Combine(_root, Scene), " invalid edit");
        var configuration = Configuration();
        await Assert.ThrowsAsync<InvalidDataException>(() => DevelopmentRegionExport.EnsureCurrentAsync(configuration, Application,
            _ => throw new InvalidDataException("Invalid collider"), TextWriter.Null));
        Assert.Equal(before, File.ReadAllBytes(Published));
        Assert.Equal(DevelopmentRegionExport.PackagePath, configuration["RegionExports:PackagePath"]);
    }

    [Fact]
    public async Task SuccessWithoutFreshPackageStillStopsStartup()
    {
        File.AppendAllText(Path.Combine(_root, Scene), " edited");
        await Assert.ThrowsAsync<InvalidDataException>(() => DevelopmentRegionExport.EnsureCurrentAsync(Configuration(), Application,
            _ => Task.CompletedTask, TextWriter.Null));
    }

    [Theory]
    [InlineData(false, DevelopmentRegionExport.PackagePath)]
    [InlineData(true, "Data/Regions/custom-package.json")]
    public async Task ExplicitOverridesDoNotAutoExport(bool enabled, string package)
    {
        File.Delete(Published);
        var configuration = Configuration(enabled, package);
        await DevelopmentRegionExport.EnsureCurrentAsync(configuration, Application,
            _ => throw new InvalidOperationException("Explicit override must be respected."), TextWriter.Null);
        Assert.Equal(package, configuration["RegionExports:PackagePath"]);
    }

    [Fact]
    public async Task StandaloneServerDoesNotRequireAuthoringTools()
    {
        File.Delete(Path.Combine(_root, "Game.slnx"));
        var configuration = Configuration();
        await DevelopmentRegionExport.EnsureCurrentAsync(configuration, Application,
            _ => throw new InvalidOperationException("Standalone server must not export."), TextWriter.Null);
        Assert.Equal(DevelopmentRegionExport.PackagePath, configuration["RegionExports:PackagePath"]);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
