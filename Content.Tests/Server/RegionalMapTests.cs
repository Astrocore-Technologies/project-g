using System.Numerics;
using System.Text.Json.Nodes;
using Content.Server.Persistence;
using Xunit;

namespace Content.Tests.Server;

public sealed class RegionalMapTests
{
    [Fact]
    public void VisitingAMarkerWithinTheSameCellDoesNotUseAnotherRegionsDiscoveryBits()
    {
        var world = StarterZoneTests.World(); var initial = world.CreateInitialCharacter(); var grid = world.Navigation;
        var map = new SavedExploration { RegionKey = "prototype", Width = grid.Width, Height = grid.Height,
            OriginX = grid.Origin.X, OriginZ = grid.Origin.Y, CellSize = grid.CellSize,
            Cells = new ulong[(grid.CellCount + 63) / 64], Tutorial = 0, Places = 0 };
        var player = world.AddPlayer(42, new(1), initial with { X = -10.6f, Z = -6,
            Progression = initial.Progression! with { Discoveries = 3, Exploration = map } });
        world.Simulate(.05f); Assert.Empty(world.ExplorationState(player.EntityId).Places);
        var oldCell = grid.Cell(player.Position);
        Assert.True(world.TryApplyMove(42, new(1, 0, new Vector2(-10.4f, -6)))); world.Simulate(.05f);
        Assert.Equal(oldCell, grid.Cell(player.Position));
        Assert.Equal((ushort)1, Assert.Single(world.ExplorationState(player.EntityId).Places).Id);
        Assert.Equal(0, world.CaptureCharacter(42).Progression!.Experience);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    public void ExplicitNullArchiveOrNullMapIsNotLegacyData(string value)
    {
        var state = StarterZoneTests.World().CreateInitialCharacter().Progression!;
        var document = JsonNode.Parse(state.Serialize())!;
        document["OtherExplorations"] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => SavedProgression.Deserialize(document.ToJsonString()));
    }

    [Fact]
    public void ArchiveRejectsDuplicateCurrentRegionAndAnUnboundedNumberOfMaps()
    {
        var world = StarterZoneTests.World(); var p = world.AddPlayer(42, new(1), world.CreateInitialCharacter());
        world.Simulate(.05f); var state = world.CaptureCharacter(42).Progression!; var map = state.Exploration!;
        Assert.Throws<InvalidDataException>(() => (state with { OtherExplorations = [map] }).Validate());
        var two = state with { OtherExplorations = [map with { RegionKey = "a" }, map with { RegionKey = "b" }] };
        Assert.Equal(2, SavedProgression.Deserialize(two.Serialize()).OtherExplorations!.Length);
        Assert.Throws<InvalidDataException>(() => (state with { OtherExplorations = [map with { RegionKey = "a" }, map with { RegionKey = "a" }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (state with { OtherExplorations = [map with { RegionKey = "a" }, map with { RegionKey = "b" }, map with { RegionKey = "c" }] }).Validate());
    }
}
