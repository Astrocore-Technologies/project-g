using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class SparseMovementTests
{
    [Fact]
    public void HeldCursorStopsAtWallButReplicatesRunningIntentUntilReleaseStop()
    {
        var world = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            Options.Create(new NavigationOptions { BlockedAreas = [new() { X = 14, Z = 10, Width = 2, Height = 10 }] }));
        var player = world.AddPlayer(1, new(1)); var start = player.Position;
        Assert.True(world.TryApplyMove(1, new(1, 0, start + Vector2.UnitX * 14, Kind: MovementCommandKind.CursorStart)));
        for (var tick = 0; tick < 100; tick++) world.Simulate(.05f);
        Assert.InRange(player.Position.X, -1.6f, -1); Assert.Equal(start.Y, player.Position.Y);
        var view = new InterestView(); world.UpdateInterest(1, view);
        Assert.True(Assert.Single(view.Snapshots).DirectCursor && view.Snapshots[0].RunningIntent);
        var stopped = player.Position;
        Assert.True(world.TryApplyMove(1, new(2, 100, default, InputOrder: 2, Kind: MovementCommandKind.Stop)));
        for (var tick = 0; tick < 20; tick++) world.Simulate(.05f);
        world.UpdateInterest(1, view);
        Assert.Equal(stopped, player.Position);
        Assert.False(view.Snapshots[0].DirectCursor || view.Snapshots[0].RunningIntent);
    }

    [Fact]
    public void DestinationAdvancesWithoutFurtherPacketsAndAcknowledgesElapsedClientTicks()
    {
        var world = Create(); var player = world.AddPlayer(1, new(1));
        var start = player.Position;
        Assert.True(world.TryApplyMove(1, new(1, 100, start + Vector2.UnitY * 5, Kind: MovementCommandKind.Destination)));
        var view = new InterestView();
        for (var tick = 0; tick < 20; tick++)
        {
            world.Simulate(.05f); world.UpdateInterest(1, view);
            Assert.Equal((uint)(100 + tick), Assert.Single(view.Snapshots).ProcessedClientTick);
        }
        Assert.True(Vector2.Distance(start, player.Position) > 1);
        Assert.Equal(1u, player.LastProcessedSequence);
    }

    [Fact]
    public void StopUsesServerPositionInsteadOfTheClientsClaimedStoppingPoint()
    {
        var world = Create(); var player = world.AddPlayer(1, new(1));
        Assert.True(world.TryApplyMove(1, new(1, 0, player.Position + Vector2.UnitY * 5, InputOrder: 10)));
        world.Simulate(.05f); var stopped = player.Position;
        Assert.True(world.TryApplyMove(1, new(2, 1, stopped - Vector2.UnitY * 3, InputOrder: 11, Kind: MovementCommandKind.Stop)));
        for (var tick = 0; tick < 20; tick++) world.Simulate(.05f);
        Assert.Equal(stopped, player.Position); Assert.Equal(stopped, player.Target);
        Assert.False(world.TryApplyMove(1, new(3, 2, stopped + Vector2.UnitY * 5, InputOrder: 10)));
    }

    [Fact]
    public void RejectedReliableDestinationIsAcknowledgedWithoutReplacingTheValidRoute()
    {
        var world = Create(); var player = world.AddPlayer(1, new(1));
        var valid = player.Position + Vector2.UnitY * 3;
        Assert.True(world.TryApplyMove(1, new(1, 0, valid, InputOrder: 10)));
        world.Simulate(.05f);
        world.ApplyReceivedMove(1, new(2, 1, new(1e6f, 1e6f), InputOrder: 11, Kind: MovementCommandKind.Destination));
        Assert.Equal(valid, player.Target); Assert.Equal(11u, player.CancelledNavigationOrder);
        Assert.False(world.TryApplyMove(1, new(3, 2, valid, InputOrder: 11)));
    }

    private static ServerWorld Create() => new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()));
}
