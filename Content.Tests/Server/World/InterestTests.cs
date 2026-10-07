using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class InterestTests
{
    [Fact]
    public void VisibilityHasHysteresisAndEmitsEachTransitionOnce()
    {
        var world = CreateWorld(2f, 3f);
        var observer = world.AddPlayer(1, new PlayerId(1));
        var other = world.AddPlayer(2, new PlayerId(2));
        var view = new InterestView();
        world.UpdateInterest(1, view);
        Assert.Contains(observer.EntityId, view.Entered);
        Assert.Contains(other.EntityId, view.Entered);

        MoveTo(world, 2, 1, new Vector2(-6.5f, 0f));
        world.UpdateInterest(1, view);
        Assert.Contains(other.EntityId, view.Entities);
        Assert.Empty(view.Entered);
        Assert.Empty(view.Left);

        MoveTo(world, 2, 2, new Vector2(-5f, 0f));
        world.UpdateInterest(1, view);
        Assert.Equal(other.EntityId, Assert.Single(view.Left));
        Assert.Equal(observer.EntityId, Assert.Single(view.Snapshots).EntityId);
        world.UpdateInterest(1, view);
        Assert.Empty(view.Left);

        MoveTo(world, 2, 3, new Vector2(-6.5f, 0f));
        world.UpdateInterest(1, view);
        Assert.DoesNotContain(other.EntityId, view.Entities);
        MoveTo(world, 2, 4, new Vector2(-7f, 0f));
        world.UpdateInterest(1, view);
        Assert.Equal(other.EntityId, Assert.Single(view.Entered));
        Assert.Equal(world.Tick, world.CreateSpawn(other.EntityId).ServerTick);
    }

    [Fact]
    public void MoreThan32RelevantEntitiesAreNotTruncated()
    {
        var world = CreateWorld(30f, 32f);
        for (var i = 1; i <= 65; i++)
            world.AddPlayer(i, new PlayerId((ulong) i));
        var view = new InterestView();
        world.UpdateInterest(65, view);

        Assert.Equal(65, view.Entities.Count);
        Assert.Equal(65, view.Snapshots.Count);
        Assert.Equal(65, view.Entered.Count);
        Assert.Contains(view.Snapshots, state => state.EntityId == new NetworkEntityId(65));
        world.UpdateInterest(65, view);
        Assert.Empty(view.Entered);
    }

    [Fact]
    public void UnknownConnectionsCannotMoveAndRuntimeIdsAreNeverReused()
    {
        var world = CreateWorld(2f, 3f);
        var previous = world.AddPlayer(1, new PlayerId(1));
        Assert.False(world.TryApplyMove(99, new MoveCommand(1, 1, Vector2.Zero)));
        Assert.Same(previous, world.RemovePlayer(1));
        Assert.Null(world.RemovePlayer(1));
        var replacement = world.AddPlayer(1, new PlayerId(2));
        Assert.NotEqual(previous.EntityId, replacement.EntityId);
    }

    [Fact]
    public void InvalidInterestSettingsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => CreateWorld(3f, 2f));
        Assert.Throws<ArgumentException>(() => CreateWorld(float.NaN, 3f));
    }

    private static ServerWorld CreateWorld(float radius, float exitRadius) => new(
        Options.Create(new MovementOptions()),
        Options.Create(new InterestOptions { Radius = radius, ExitRadius = exitRadius }));

    private static void MoveTo(ServerWorld world, int connectionId, uint sequence, Vector2 target)
    {
        Assert.True(world.TryApplyMove(connectionId, new MoveCommand(sequence, sequence, target)));
        for (var i = 0; i < 100; i++)
            world.Simulate(0.05f);
    }
}
