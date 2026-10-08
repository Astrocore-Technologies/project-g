using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class NpcSimulationTests
{
    private static ServerWorld Create(NpcOptions? npc = null, NavigationOptions? navigation = null) => new(
        Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
        Options.Create(navigation ?? new NavigationOptions()), ContentCatalogTests.Load(),
        npc: Options.Create(npc ?? new NpcOptions { Enabled = true, X = -4, Z = 0 }));

    [Fact]
    public void MonsterAcquiresChasesTelegraphsAndDamagesOnlyOnRelease()
    {
        var world = Create(); var player = world.AddPlayer(42, new(1));
        var hp = world.Combat!.Get(player.EntityId).Health;
        for (var i = 0; i < 80 && world.Npc!.Behavior != NpcBehavior.Windup; i++) world.Simulate(0.05f);
        Assert.Equal(NpcBehavior.Windup, world.Npc!.Behavior);
        Assert.Equal(player.EntityId, world.Npc.TargetId);
        Assert.Equal(hp, world.Combat.Get(player.EntityId).Health);
        Assert.True(world.Npc.Telegraph!.Value.RemainingSeconds > 0);
        for (var i = 0; i < 30; i++) world.Simulate(0.05f);
        Assert.True(world.Combat.Get(player.EntityId).Health < hp);
        Assert.Equal(world.Combat.Get(world.TrainingTargetId).Stats.MaxHealth, world.Combat.Get(world.TrainingTargetId).Health);
    }

    [Fact]
    public void LockedWindupCanBeDodgedAndDoesNotTrackOrTeleport()
    {
        var world = Create(); var player = world.AddPlayer(42, new(1));
        for (var i = 0; i < 80 && world.Npc!.Behavior != NpcBehavior.Windup; i++) world.Simulate(0.05f);
        var hp = world.Combat!.Get(player.EntityId).Health;
        var origin = world.Npc!.Motion.Position;
        var locked = world.Npc.Telegraph!.Value.Direction;
        Assert.True(world.TryQueueAbility(42, new(1, 0, 3, -locked), 0));
        for (var i = 0; i < 20; i++)
        {
            world.Simulate(0.05f);
            Assert.Equal(hp, world.Combat.Get(player.EntityId).Health);
            if (world.Npc.Behavior == NpcBehavior.Windup) Assert.Equal(origin, world.Npc.Motion.Position);
        }
    }

    [Fact]
    public void DisconnectAndLeashTriggerWalkHomeWithoutHealingOrReacquiringEarly()
    {
        var world = Create(); var player = world.AddPlayer(42, new(1));
        for (var i = 0; i < 5; i++) world.Simulate(0.05f);
        Assert.Equal(player.EntityId, world.Npc!.TargetId);
        world.RemovePlayer(42);
        for (var i = 0; i < 80; i++) world.Simulate(0.05f);
        Assert.Equal(world.Npc.Home, world.Npc.Motion.Position);
        Assert.Equal(NpcBehavior.Idle, world.Npc.Behavior);
        Assert.False(world.Npc.TargetId.IsValid);
        Assert.Equal(0, world.Npc.Telegraph?.RemainingSeconds ?? 0);
    }

    [Fact]
    public void KillingNpcDuringWindupCancelsStrikeAndNoAutomaticRespawnOccurs()
    {
        var world = Create(); var player = world.AddPlayer(42, new(1));
        for (var i = 0; i < 80 && world.Npc!.Behavior != NpcBehavior.Windup; i++) world.Simulate(0.05f);
        var hp = world.Combat!.Get(player.EntityId).Health;
        world.Combat.ApplyAbilityDamage(world.Npc!.Id, 100000);
        for (var i = 0; i < 100; i++) world.Simulate(0.05f);
        Assert.Equal(NpcBehavior.Defeated, world.Npc.Behavior);
        Assert.Equal(0, world.Combat.Get(world.Npc.Id).Health);
        Assert.Equal(hp, world.Combat.Get(player.EntityId).Health);
        Assert.Empty(world.Combat.Events);
        Assert.Equal(0, world.Npc.Telegraph!.Value.RemainingSeconds);
        Assert.False(world.Combat.Queue(world.Npc.Id, new(1, 0, Vector2.UnitX), world.Tick));
    }

    [Fact]
    public void LeavingLeashMakesNpcReturnRatherThanChaseAcrossRegion()
    {
        var world = Create(); world.AddPlayer(42, new(1));
        world.Simulate(0.05f);
        Assert.True(world.Npc!.TargetId.IsValid);
        Assert.True(world.TryApplyMove(42, new(1, 0, new(13, 13))));
        for (var i = 0; i < 200; i++) world.Simulate(0.05f);
        Assert.False(world.Npc.TargetId.IsValid);
        Assert.Equal(world.Npc.Home, world.Npc.Motion.Position);
        Assert.Equal(NpcBehavior.Idle, world.Npc.Behavior);
    }

    [Fact]
    public void DefeatedPlayersCannotMoveAttackOrDashAndAreNoLongerTargets()
    {
        var world = Create(); var player = world.AddPlayer(42, new(1));
        for (var i = 0; i < 1000 && world.Combat!.Get(player.EntityId).Health > 0; i++) world.Simulate(0.05f);
        Assert.Equal(0, world.Combat!.Get(player.EntityId).Health);
        var position = player.Position;
        Assert.False(world.TryApplyMove(42, new(1, 0, new(10, 10))));
        world.TryQueueAbility(42, new(1, 0, 3, Vector2.UnitX), 0);
        world.TryQueueAttack(42, new(1, 0, Vector2.UnitX));
        world.Simulate(0.05f);
        Assert.Equal(AbilityOutcome.InvalidState, world.Abilities!.Results[player.EntityId].Outcome);
        Assert.Equal(position, player.Position);
        for (var i = 0; i < 100; i++) world.Simulate(0.05f);
        Assert.False(world.Npc!.TargetId.IsValid);
    }

    [Fact]
    public void MonstersParticipateInAoiAndMovementSnapshotsWithoutFakePlayerIds()
    {
        var world = Create(); world.AddPlayer(42, new(1)); var view = new InterestView();
        world.UpdateInterest(42, view);
        Assert.Contains(world.Npc!.Id, view.Entered);
        Assert.False(world.IsPlayer(world.Npc.Id));
        Assert.Contains(view.Snapshots, state => state.EntityId == world.Npc.Id);
        Assert.Equal(CombatEntityKind.Monster, world.Combat!.State(world.Npc.Id, 0).Kind);
    }

    [Fact]
    public void ChasingRepathsAroundWallAndNeverCrossesBlockedGeometry()
    {
        var world = Create(new NpcOptions { Enabled = true, X = -3, Z = 0 }, new NavigationOptions
        {
            BlockedAreas = new() { new BlockedAreaOptions { X = 14, Z = 10, Width = 2, Height = 10 } }
        });
        var player = world.AddPlayer(42, new(1));
        world.Simulate(0.05f);
        Assert.Equal(player.EntityId, world.Npc!.TargetId);
        // Move the test actor coherently; the NPC must retain a target that went behind an obstacle.
        Assert.True(world.TryApplyMove(42, new(1, 0, new(3, 0))));
        var detour = false;
        for (var i = 0; i < 150; i++)
        {
            world.Simulate(0.05f);
            Assert.True(world.Navigation.IsWalkable(world.Npc.Motion.Position));
            detour |= Math.Abs(world.Npc.Motion.Position.Y) > 5;
        }
        Assert.True(detour);
    }

    [Fact]
    public void InvalidNpcConfigurationFailsBeforeNetworking()
    {
        Assert.Throws<ArgumentException>(() => Create(new NpcOptions { Enabled = true, AggroRadius = float.NaN }));
        Assert.Throws<ArgumentException>(() => Create(new NpcOptions { Enabled = true, WindupSeconds = 0 }));
        Assert.Throws<ArgumentException>(() => Create(new NpcOptions { Enabled = true, X = 100 }));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SameFixedHealthMonsterCanBeDefeatedSoloOrTogether(bool together)
    {
        var world = Create(new NpcOptions { Enabled = true, X = -3, Z = 0, AggroRadius = 0.1f });
        var first = world.AddPlayer(42, new(1));
        var startingHealth = world.Combat!.Get(world.Npc!.Id).Health;
        if (together) world.AddPlayer(43, new(2));
        Assert.Equal(startingHealth, world.Combat.Get(world.Npc.Id).Health);
        for (uint seq = 1; seq <= 8 && world.Combat.Get(world.Npc.Id).Health > 0; seq++)
        {
            world.TryQueueAttack(42, new(seq, 0, Vector2.UnitX));
            if (together) world.TryQueueAttack(43, new(seq, 0, Vector2.UnitY));
            for (var i = 0; i < 30; i++) world.Simulate(0.05f);
        }
        Assert.Equal(0, world.Combat.Get(world.Npc.Id).Health);
        Assert.Equal(NpcBehavior.Defeated, world.Npc.Behavior);
        Assert.True(world.Combat.Get(first.EntityId).Health > 0);
    }
}
