using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class ServerWorldTests
{
    [Fact]
    public void ServerLimitsMovementToConfiguredSpeed()
    {
        var world = CreateWorld();
        var player = world.AddPlayer(1, new PlayerId(1));
        var start = player.Position;

        Assert.True(world.TryApplyMove(1, new MoveCommand(1, 1, new Vector2(10f, 0f))));
        world.Simulate(0.05f);

        Assert.Equal(3.5f * .05f, Vector2.Distance(start, player.Position), 4);
    }

    [Fact]
    public void InvalidAndOutOfOrderCommandsAreRejected()
    {
        var world = CreateWorld();
        var player = world.AddPlayer(1, new PlayerId(1));

        Assert.False(world.TryApplyMove(
            1,
            new MoveCommand(1, 1, new Vector2(float.NaN, 0f))));
        Assert.False(world.TryApplyMove(
            1,
            new MoveCommand(1, 1, new Vector2(100f, 0f))));
        Assert.True(world.TryApplyMove(
            1,
            new MoveCommand(2, 2, new Vector2(0f, 0f))));
        Assert.False(world.TryApplyMove(
            1,
            new MoveCommand(1, 3, new Vector2(1f, 0f))));
        Assert.Equal((uint) 2, player.LastProcessedSequence);
    }

    [Fact]
    public void DisconnectRemovesPlayerFromSnapshots()
    {
        var world = CreateWorld();
        var player = world.AddPlayer(1, new PlayerId(1));
        var observer = world.AddPlayer(2, new PlayerId(2));
        var view = new InterestView();
        world.UpdateInterest(observer.ConnectionId, view);
        Assert.Contains(view.Snapshots, state => state.EntityId == player.EntityId);

        world.RemovePlayer(1);

        world.UpdateInterest(observer.ConnectionId, view);
        Assert.DoesNotContain(view.Snapshots, state => state.EntityId == player.EntityId);
        Assert.Contains(player.EntityId, view.Left);
    }

    private static ServerWorld CreateWorld() =>
        new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()));
}
