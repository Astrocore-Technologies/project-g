using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Tests.Server.Data;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class PlayerVisibilityTests
{
    [Fact]
    public void OpaqueWallExcludesAnotherPlayerFromSpawnAndSnapshotButNeverSelf()
    {
        var world = Create(); var observer = world.AddPlayer(1, new(1)); var other = world.AddPlayer(2, new(2));
        var view = new InterestView(); world.UpdateInterest(1, view);
        Assert.Equal(observer.EntityId, Assert.Single(view.Entities));
        Assert.Equal(observer.EntityId, Assert.Single(view.Entered));
        Assert.Equal(observer.EntityId, Assert.Single(view.Snapshots).EntityId);
        Assert.DoesNotContain(other.EntityId, view.Entities);
        var reverse = new InterestView(); world.UpdateInterest(2, reverse);
        Assert.Equal(other.EntityId, Assert.Single(reverse.Entities));
    }

    [Fact]
    public void LosingSightDespawnsOnceAndRegainingSightSendsFreshBaselineInsideSameAoi()
    {
        var world = Create(); world.AddPlayer(1, new(1)); var other = world.AddPlayer(2, new(2));
        var view = new InterestView(); world.UpdateInterest(1, view);
        Move(world, 1, new(-7, 6)); world.UpdateInterest(1, view);
        Assert.Equal(other.EntityId, Assert.Single(view.Entered));
        Assert.Contains(view.Snapshots, state => state.EntityId == other.EntityId && state.Position == other.Position);
        world.UpdateInterest(1, view); Assert.Empty(view.Entered);
        Move(world, 2, new(-7, 0)); world.UpdateInterest(1, view);
        Assert.Equal(other.EntityId, Assert.Single(view.Left));
        Assert.DoesNotContain(view.Snapshots, state => state.EntityId == other.EntityId);
        world.UpdateInterest(1, view); Assert.Empty(view.Left);
        Move(world, 3, new(-7, 6)); world.UpdateInterest(1, view);
        Assert.Equal(other.EntityId, Assert.Single(view.Entered));
        Assert.Equal(world.Tick, world.CreateSpawn(other.EntityId).ServerTick);
    }

    [Fact]
    public void VisibilityUsesUnpaddedBodyRayAndTheRealHeightOfOpaqueGeometry()
    {
        // A low curb must not hide a player just because it blocks walking.
        var world = Create(wallHeight: .25f); world.AddPlayer(1, new(1)); var other = world.AddPlayer(2, new(2));
        var view = new InterestView(); world.UpdateInterest(1, view);
        Assert.Contains(other.EntityId, view.Entities);
    }

    [Fact]
    public void APhysicalFloorBlocksSightBetweenVerticallySeparatedPlayers()
    {
        var world = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            Options.Create(new NavigationOptions { ExportedSurface = Map(3, ceiling: true) }), catalog: ContentCatalogTests.Load());
        var initial = world.CreateInitialCharacter();
        var observer = world.AddPlayer(1, new(1), initial with { X = -9, Z = 0, Surface = new(1, 0, world.GeometryHash) });
        var other = world.AddPlayer(2, new(2), initial with { X = -7, Z = 0, Surface = new(1, 4, world.GeometryHash) });
        var view = new InterestView(); world.UpdateInterest(1, view);
        Assert.Contains(observer.EntityId, view.Entities);
        Assert.DoesNotContain(other.EntityId, view.Entities);
        var upstairs = world.AddPlayer(3, new(3), initial with { X = -9, Z = 0, Surface = new(1, 4, world.GeometryHash) });
        var reverse = new InterestView(); world.UpdateInterest(2, reverse);
        Assert.Contains(upstairs.EntityId, reverse.Entities);
        Assert.DoesNotContain(observer.EntityId, reverse.Entities);
    }

    private static ServerWorld Create(float wallHeight = 3) => new(Options.Create(new MovementOptions()),
        Options.Create(new InterestOptions { Radius = 20, ExitRadius = 24 }),
        Options.Create(new NavigationOptions { ExportedSurface = Map(wallHeight) }));

    private static SurfaceGeometry Map(float wallHeight, bool ceiling = false)
    {
        var map = new SurfaceGeometry { Version = 3, ScenePath = "res://Scenes/Regions/VisibilityTest.tscn", SourceHash = new string('A', 64),
            Vertices = [new(-15, 0, -15), new(15, 0, -15), new(15, 0, 15), new(-15, 0, 15),
                new(-15, 4, -15), new(15, 4, -15), new(15, 4, 15), new(-15, 4, 15)],
            Polygons = ceiling ? [[0, 1, 2, 3], [4, 5, 6, 7]] : [[0, 1, 2, 3]], WalkableFacesOcclude = ceiling,
            Occluders = ceiling
                ? [new(new(-8, wallHeight / 2, 0), new(.4f, wallHeight, 4), new(0, 0, 0)), new(new(-8, 3, 0), new(5, .2f, 5), new(0, 0, 0))]
                : [new(new(-8, wallHeight / 2, 0), new(.4f, wallHeight, 4), new(0, 0, 0))] };
        map.Seal(); return map;
    }

    private static void Move(ServerWorld world, uint sequence, Vector2 target)
    {
        Assert.True(world.TryApplyMove(2, new(sequence, sequence * 200, target, GeometryHash: world.GeometryHash)));
        for (var i = 0; i < 100; i++) world.Simulate(.05f);
    }
}
