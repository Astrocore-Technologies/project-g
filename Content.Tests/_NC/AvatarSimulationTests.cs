using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;

public sealed class AvatarSimulationTests
{
    private static ServerWorld World() => new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
        catalog:ContentCatalogTests.Load(), combat:Options.Create(new CombatOptions()));
    [Fact]
    public void EmotesValidateOwnershipReplayBusyAndCancelOnMovement()
    {
        var world=World();var player=world.AddPlayer(1,new(1));world.AddPlayer(2,new(2));
        Assert.False(world.TryQueueEmote(99,new(1,AvatarGesture.Wave)));
        Assert.True(world.TryQueueEmote(1,new(1,AvatarGesture.Wave)));
        Assert.False(world.TryQueueEmote(1,new(1,AvatarGesture.Cheer)));
        Assert.False(world.TryQueueEmote(1,new(2,AvatarGesture.Cheer))); // One queued intention per actor.
        world.Simulate(.05f);
        Assert.Equal(AvatarGesture.Wave,world.Avatar(player.EntityId).Gesture);
        Assert.False(world.TryQueueEmote(1,new(3,AvatarGesture.Cheer))); // Throttle cannot be bypassed with a new sequence.
        Assert.True(world.TryQueueEmote(1,new(4,AvatarGesture.None)));world.Simulate(.05f);
        Assert.Equal(AvatarGesture.None,world.Avatar(player.EntityId).Gesture);
        for(var i=0;i<20;i++)world.Simulate(.05f);
        Assert.True(world.TryQueueEmote(1,new(5,AvatarGesture.Sit)));world.Simulate(.05f);
        Assert.Equal(AvatarGesture.Sit,world.Avatar(player.EntityId).Gesture);
        Assert.True(world.TryApplyMove(1,new(1,1,player.Position+new Vector2(1,0))));world.Simulate(.05f);
        Assert.Equal(AvatarGesture.None,world.Avatar(player.EntityId).Gesture);
        var old=player.EntityId;world.RemovePlayer(1);var replacement=world.AddPlayer(1,new(3));
        Assert.NotEqual(old,replacement.EntityId);Assert.Equal(AvatarGesture.None,world.Avatar(replacement.EntityId).Gesture);
    }
    [Fact]
    public void DefensePublicStateContainsObservablePoseAndDeadPlayerCannotEmote()
    {
        var world=World();var player=world.AddPlayer(1,new(1));
        Assert.True(world.TryQueueDefense(1,new(1,DefenseAction.Block,Vector2.UnitX)));world.Simulate(.05f);
        var state=world.Avatar(player.EntityId);Assert.True(state.Blocking);Assert.Equal(Vector2.UnitX,state.Facing);
        Assert.True(world.TryQueueEmote(1,new(1,AvatarGesture.Wave)));world.Simulate(.05f);
        Assert.Equal(AvatarGesture.None,world.Avatar(player.EntityId).Gesture);
        world.Combat!.Get(player.EntityId).Health=0;
        Assert.True(world.TryQueueEmote(1,new(2,AvatarGesture.Wave)));world.Simulate(.05f);
        Assert.Equal(AvatarGesture.None,world.Avatar(player.EntityId).Gesture);
    }
}
