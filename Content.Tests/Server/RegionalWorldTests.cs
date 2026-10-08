using Content.Server.Configuration;
using Content.Server.Regions;
using Content.Server.World;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class RegionalWorldTests
{
    private static ServerWorld World(string region, RuntimeEntityAllocator ids) => new(
        Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
        catalog: ContentCatalogTests.Load(), regionId: region, entityIds: ids);

    [Fact]
    public void RegionsShareRuntimeAllocationButNotSpatialActors()
    {
        var ids = new RuntimeEntityAllocator();
        var source = World("prototype", ids); var target = World("outskirts", ids);
        var a = source.AddPlayer(1, new(1), source.CreateInitialCharacter());
        var b = target.AddPlayer(2, new(2), target.CreateInitialCharacter());
        Assert.NotEqual(source.TrainingTargetId, target.TrainingTargetId);
        Assert.NotEqual(a.EntityId, b.EntityId);
        Assert.False(source.IsPlayer(b.EntityId)); Assert.False(target.IsPlayer(a.EntityId));
        Assert.Equal("prototype", source.CaptureCharacter(1).RegionId);
        Assert.Equal("outskirts", target.CaptureCharacter(2).RegionId);
        source.RemovePlayer(1);
        Assert.NotEqual(a.EntityId, source.AddPlayer(1, new(1)).EntityId);
    }

    [Fact]
    public void CompatibleCoordinatesDoNotAuthorizeLoadingTheWrongRegion()
    {
        var ids = new RuntimeEntityAllocator();
        var source = World("prototype", ids); var target = World("outskirts", ids);
        var saved = source.CreateInitialCharacter();
        Assert.Throws<InvalidDataException>(() => target.AddPlayer(1, new(1), saved));
        Assert.Empty(target.Players);
        var serialized = (saved with { RegionId = "outskirts" }).Serialize();
        var restored = Content.Server.Persistence.CharacterState.Deserialize(serialized);
        target.AddPlayer(1, new(1), restored);
        Assert.Equal("outskirts", target.CaptureCharacter(1).RegionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../prototype")]
    [InlineData("Prototype")]
    [InlineData("регион")]
    public void InvalidRegionKeysFailBeforeSimulation(string region)
    {
        Assert.Throws<ArgumentException>(() => World(region, new()));
        var world = World("prototype", new());
        Assert.Throws<InvalidDataException>(() => (world.CreateInitialCharacter() with { RegionId = region }).Validate());
    }
}
