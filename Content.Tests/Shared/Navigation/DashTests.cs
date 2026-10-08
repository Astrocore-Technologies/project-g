using System.Numerics;
using Content.Server.Configuration;
using Content.Shared.Combat;
using Content.Shared.Navigation;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class DashTests
{
    [Fact]
    public void SweptProjectileCannotTunnelPastTargetBetweenTicks()
    {
        Assert.True(SweptCircle.TryHit(new(-5, 0), new(5, 0), Vector2.Zero, 0.5f, out var time));
        Assert.Equal(0.45f, time, 5);
        Assert.False(SweptCircle.TryHit(new(-5, 2), new(5, 2), Vector2.Zero, 0.5f, out _));
        Assert.True(SweptCircle.TryHit(Vector2.Zero, Vector2.Zero, Vector2.Zero, 0.5f, out time));
        Assert.Equal(0, time);
    }

    [Fact]
    public void StraightDashStopsAtWallRatherThanFindingDetour()
    {
        var settings = new MovementOptions().ToSettings();
        var grid = new NavigationOptions
        {
            BlockedAreas = new() { new BlockedAreaOptions { X = 14, Z = 10, Width = 2, Height = 10 } }
        }.CreateGrid(settings);
        var start = new Vector2(-2, 0);
        Assert.True(DashGeometry.TryDestination(grid, start, Vector2.UnitX, 6, out var end));
        Assert.True(end.X < -1);
        Assert.True(grid.CanTraverse(start, end));
        var motion = new NavigationMover(grid, settings, new NavigationPathfinder(grid), start);
        Assert.True(motion.TryStartDash(end, 15));
        Assert.False(motion.TryStartDash(new(4, 0), 15));
        for (var i = 0; i < 10; i++) motion.Step(0.05f);
        Assert.Equal(end, motion.Position);
        Assert.False(motion.IsMoving);
    }

    [Fact]
    public void SnapshotRestoreReplaysRemainingDashAndEndsAtSameDestination()
    {
        var settings = new MovementOptions().ToSettings();
        var grid = new NavigationOptions().CreateGrid(settings);
        var server = new NavigationMover(grid, settings, new NavigationPathfinder(grid), new(-7, 0));
        var predicted = new NavigationMover(grid, settings, new NavigationPathfinder(grid), new(-7, 0));
        Assert.True(server.TryStartDash(new(-4, 0), 15));
        server.Step(0.05f);
        Assert.True(predicted.Restore(server.Position, server.Target, server.DashDestination, server.DashSpeed));
        for (var i = 0; i < 10; i++) { server.Step(0.05f); predicted.Step(0.05f); }
        Assert.Equal(server.Position, predicted.Position);
        Assert.False(predicted.IsDashing);
    }
}
