using Content.Server.Regions;
using Content.Server.Data;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Configuration;

namespace Content.Tests.Server;

/// <summary>Retired map data remains test-only coverage for travel, repairs and old saves.</summary>
internal static class LegacyRegionFixtures
{
    internal static RegionalWorlds Load(string? startingRegion = null, ContentCatalog? catalog = null)
        => RegionalWorlds.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "retired-authored-regions.json"),
            Configuration(startingRegion), catalog ?? ContentCatalogTests.Load());

    internal static IConfigurationRoot Configuration(string? startingRegion = null)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(fixtures, "legacy-settings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RegionExports:VerifySources"] = "false",
                ["RegionExports:PackagePath"] = Path.Combine(fixtures, "legacy-flat-package.json"),
                ["RegionExports:SurfacePackagePath"] = Path.Combine(fixtures, "legacy-surface-package.json"),
                ["Server:StartingRegion"] = startingRegion
            }).Build();
        return configuration;
    }
}
