using System.Numerics;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class NavigationTests
{
    private static readonly MovementSettings Settings = new(5f, 0.1f, -10f, 10f, -10f, 10f);

    [Fact]
    public void RouteGoesAroundWallWithoutCrossingSolidCells()
    {
        var grid = CreateGrid();
        var path = new List<Vector2>();
        var start = new Vector2(-5f, 0f);
        var target = new Vector2(5f, 0f);
        Assert.False(grid.CanTraverse(start, target));
        Assert.True(new NavigationPathfinder(grid).TryFindPath(start, target, path));
        Assert.True(path.Count >= 2);
        foreach (var waypoint in path)
        {
            Assert.True(grid.CanTraverse(start, waypoint));
            start = waypoint;
        }
        Assert.Equal(target, start);
    }

    [Fact]
    public void BlockedAndUnreachableTargetsAreRejected()
    {
        var path = new List<Vector2>();
        var grid = CreateGrid();
        var finder = new NavigationPathfinder(grid);
        Assert.False(finder.TryFindPath(new Vector2(-5f, 0f), Vector2.Zero, path));
        Assert.Empty(path);
        Assert.False(finder.TryFindPath(new Vector2(-5f, 0f), new Vector2(float.NaN, 0f), path));
        grid = CreateGrid(fullWall: true);
        Assert.False(new NavigationPathfinder(grid).TryFindPath(new Vector2(-5f, 0f), new Vector2(5f, 0f), path));
        Assert.Empty(path);
    }

    [Fact]
    public void CollisionIncludesRadiusCornersBoundsAndFullSweep()
    {
        var grid = CreateGrid();
        Assert.False(grid.IsWalkable(new Vector2(-1.2f, 0f)));
        Assert.False(grid.IsWalkable(new Vector2(-10f, 0f)));
        Assert.False(grid.CanTraverse(new Vector2(-5f, 0f), new Vector2(5f, 0f)));
        Assert.True(grid.IsWalkable(new Vector2(-1.6f, 0f)));
        Assert.True(grid.CanTraverse(new Vector2(-5f, -6f), new Vector2(5f, -6f)));
        // Expanded-box corners reject a diagonal that cuts through the wall clearance.
        Assert.False(grid.CanTraverse(new Vector2(-2f, -2.9f), new Vector2(-0.8f, -4f)));
    }

    [Fact]
    public void RouteFollowingAndPredictionAreDeterministicAndSpeedBounded()
    {
        var grid = CreateGrid();
        var start = new Vector2(-5f, 0f);
        var server = new NavigationMover(grid, Settings, new NavigationPathfinder(grid), start);
        var client = new NavigationMover(grid, Settings, new NavigationPathfinder(grid), start);
        var target = new Vector2(5f, 0f);
        Assert.True(server.TrySetTarget(target));
        Assert.True(client.TrySetTarget(target));
        for (var tick = 0; tick < 160; tick++)
        {
            var before = server.Position;
            server.Step(0.05f);
            client.Step(0.05f);
            Assert.Equal(server.Position, client.Position);
            Assert.True(Vector2.Distance(before, server.Position) <= 0.25001f);
            Assert.True(grid.CanTraverse(before, server.Position));
        }
        Assert.Equal(target, server.Position);
        Assert.False(server.IsMoving);
    }

    [Fact]
    public void ReconciliationReplaysPendingFramesAroundObstacle()
    {
        var grid = CreateGrid();
        var start = new Vector2(-5f, 0f);
        var server = new NavigationMover(grid, Settings, new NavigationPathfinder(grid), start);
        var client = new NavigationMover(grid, Settings, new NavigationPathfinder(grid), start);
        var target = new Vector2(5f, 0f);
        server.TrySetTarget(target);
        client.TrySetTarget(target);
        for (var i = 0; i < 20; i++) server.Step(0.05f);
        // Introduce prediction drift, restore authority, then replay five pending ticks.
        for (var i = 0; i < 27; i++) client.Step(0.05f);
        Assert.True(client.Reset(server.Position, server.Target));
        Assert.Equal(server.Position, client.Position);
        for (var i = 0; i < 5; i++)
        {
            server.Step(0.05f);
            client.Step(0.05f);
            Assert.True(grid.IsWalkable(client.Position));
        }
        Assert.True(Vector2.Distance(server.Position, client.Position) < 0.1f);
    }

    [Fact]
    public void LargeTickFollowsRouteWithoutTunnelingAndInvalidInputKeepsState()
    {
        var grid = CreateGrid();
        var mover = new NavigationMover(grid, Settings, new NavigationPathfinder(grid), new Vector2(-5f, 0f));
        Assert.True(mover.TrySetTarget(new Vector2(5f, 0f)));
        var target = mover.Target;
        Assert.False(mover.TrySetTarget(Vector2.Zero));
        Assert.Equal(target, mover.Target);
        var before = mover.Position;
        mover.Step(float.NaN);
        Assert.Equal(before, mover.Position);
        mover.Step(10f);
        Assert.Equal(target, mover.Position);
        Assert.True(grid.IsWalkable(mover.Position));
    }

    [Fact]
    public void GridCopiesWireDataAndFindsSafeSpawn()
    {
        var original = CreateGrid().ToMessage();
        var grid = new NavigationGrid(original);
        Array.Clear(original.BlockedCells);
        Assert.False(grid.IsWalkable(Vector2.Zero));
        Assert.True(grid.TryFindSpawn(Vector2.Zero, out var spawn));
        Assert.True(grid.IsWalkable(spawn));
    }

    [Fact]
    public void RepeatedDestinationAndRouteFollowingAllocateNothingAfterWarmup()
    {
        var grid = CreateGrid();
        var mover = new NavigationMover(grid, Settings, new NavigationPathfinder(grid), new Vector2(-5f, 0f));
        var target = new Vector2(5f, 0f);
        Assert.True(mover.TrySetTarget(target));
        mover.Step(0.05f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            mover.TrySetTarget(target);
            mover.Step(0.05f);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static NavigationGrid CreateGrid(bool fullWall = false)
    {
        var cells = new byte[400];
        for (var z = fullWall ? 0 : 7; z < (fullWall ? 20 : 13); z++)
        {
            cells[z * 20 + 9] = 1;
            cells[z * 20 + 10] = 1;
        }
        return new NavigationGrid(new RegionNavigation(new Vector2(-10f), 1f, 0.45f, 20, 20, cells));
    }
}
