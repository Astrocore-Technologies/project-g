using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Server.Tests.World;

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

        Assert.Equal(0.25f, Vector2.Distance(start, player.Position), 4);
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
        Assert.Contains(world.CreateSnapshot().Entities, state => state.EntityId == player.EntityId);

        world.RemovePlayer(1);

        Assert.DoesNotContain(world.CreateSnapshot().Entities, state => state.EntityId == player.EntityId);
    }

    private static ServerWorld CreateWorld() =>
        new(Options.Create(new MovementOptions()));
}
