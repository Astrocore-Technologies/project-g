using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class NavigationWorldTests
{
    [Fact]
    public void ServerMovesAroundWallAndRejectsBlockedDestination()
    {
        var world = CreateWorld();
        var player = world.AddPlayer(1, new PlayerId(1));
        var start = player.Position;
        Assert.False(world.TryApplyMove(1, new MoveCommand(1, 1, Vector2.Zero)));
        Assert.Equal(start, player.Position);
        world.Simulate(0.05f);
        var target = new Vector2(8f, 0f);
        Assert.True(world.TryApplyMove(1, new MoveCommand(2, 2, target)));
        var wentAround = false;
        for (var i = 0; i < 160; i++)
        {
            var previous = player.Position;
            world.Simulate(0.05f);
            Assert.True(world.Navigation.CanTraverse(previous, player.Position));
            wentAround |= MathF.Abs(player.Position.Y) > 5f;
        }
        Assert.True(wentAround);
        Assert.Equal(target, player.Position);
    }

    [Fact]
    public void SpawnNeverAppearsInsideWallAndRouteRequestsAreTickBounded()
    {
        var world = CreateWorld();
        for (var i = 1; i <= 10; i++)
            Assert.True(world.Navigation.IsWalkable(world.AddPlayer(i, new PlayerId((ulong) i)).Position));
        Assert.True(world.TryApplyMove(1, new MoveCommand(1, 1, new Vector2(-8f, 0f))));
        Assert.False(world.TryApplyMove(1, new MoveCommand(2, 2, new Vector2(-7f, 0f))));
        // Identical destinations acknowledge new sequences without rerunning A*.
        Assert.True(world.TryApplyMove(1, new MoveCommand(3, 3, new Vector2(-8f, 0f))));
        world.Simulate(0.05f);
        Assert.True(world.TryApplyMove(1, new MoveCommand(4, 4, new Vector2(-7f, 0f))));
    }

    [Fact]
    public void InvalidMapConfigurationFailsAtLoad()
    {
        Assert.Throws<ArgumentException>(() => new NavigationOptions
        {
            BlockedAreas = new() { new BlockedAreaOptions { X = 29, Z = 1, Width = 2, Height = 1 } }
        }.CreateGrid(new MovementOptions().ToSettings()));
        Assert.Throws<ArgumentException>(() => new NavigationOptions { CellSize = 0.01f }
            .CreateGrid(new MovementOptions().ToSettings()));
    }

    [Fact]
    public void UnreachableIntentDoesNotReplaceAuthoritativeDestination()
    {
        var world = new ServerWorld(Options.Create(new MovementOptions()),
            Options.Create(new InterestOptions()), Options.Create(new NavigationOptions
            {
                BlockedAreas = new() { new BlockedAreaOptions { X = 14, Z = 0, Width = 2, Height = 30 } }
            }));
        var player = world.AddPlayer(1, new PlayerId(1));
        var start = player.Position;
        Assert.False(world.TryApplyMove(1, new MoveCommand(1, 1, new Vector2(8f, 0f))));
        for (var i = 0; i < 40; i++) world.Simulate(0.05f);
        Assert.Equal(start, player.Position);
        Assert.Equal(start, player.Target);
        Assert.Equal((uint) 0, player.LastProcessedSequence);
    }

    private static ServerWorld CreateWorld() => new(
        Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
        Options.Create(new NavigationOptions
        {
            BlockedAreas = new() { new BlockedAreaOptions { X = 14, Z = 10, Width = 2, Height = 10 } }
        }));
}
