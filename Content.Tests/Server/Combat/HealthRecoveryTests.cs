using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class HealthRecoveryTests
{
    private static ServerWorld World() => new(Options.Create(new MovementOptions()),
        Options.Create(new InterestOptions()), catalog: ContentCatalogTests.Load());

    private static void Step(ServerWorld world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            world.Combat!.ClearResults();
            world.Abilities!.ClearResults();
            world.Simulate(.05f);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void InjuredPlayerRecoversFromVitalityOncePerSecondAndMarksDurableState(int vitality)
    {
        var world = World();
        var initial = world.CreateInitialCharacter();
        var player = world.AddPlayer(42, new(1), initial with { Health = 10, Stats = new(1, 1, vitality, 1, 1, 1) });
        var actor = world.Combat!.Get(player.EntityId);
        Step(world, 19);
        Assert.Equal(10, actor.Health);
        Step(world, 1);
        Assert.Equal(10 + 1 + vitality / 30d, actor.Health, 8);
        Assert.Contains(42, world.PersistenceDirty);
        Assert.Contains(player.EntityId, world.Combat.EquipmentDirty);
        Assert.Equal(actor.Health, world.CaptureCharacter(42).Health);
    }

    [Fact]
    public void DamageSchedulesHealingDuringMovementCastingAndFurtherDamage()
    {
        var world = World();
        var player = world.AddPlayer(42, new(1));
        var actor = world.Combat!.Get(player.EntityId);
        world.Combat.DamagePermission = (_, _) => true;
        world.Combat.ApplyAbilityDamage(player.EntityId, 10, world.TrainingTargetId);
        var damaged = actor.Health;
        Assert.True(world.TryApplyMove(42, new(1, 0, new(8, 0))));
        actor.IsCasting = true;
        Step(world, 10);
        var secondHit = world.Combat.ApplyAbilityDamage(player.EntityId, 1, world.TrainingTargetId);
        Step(world, 10);
        Assert.Equal(damaged - secondHit + actor.Stats.HealthRecovery, actor.Health, 8);
        Assert.NotEqual(new Vector2(-7, 0), player.Position);
    }

    [Fact]
    public void MaximumDeadOfflineAndNonPlayersNeverReceiveExtraHealth()
    {
        var world = World(); var initial = world.CreateInitialCharacter();
        var full = world.AddPlayer(1, new(1));
        var nearFull = world.AddPlayer(2, new(2), initial with { Health = initial.Health - .1 });
        var dead = world.AddPlayer(3, new(3), initial with { Health = 0 });
        var offline = world.AddPlayer(4, new(4), initial with { Health = 10 });
        world.DisconnectPvp(4);
        world.Combat!.ApplyAbilityDamage(world.TrainingTargetId, 10);
        var dummyHealth = world.Combat.Get(world.TrainingTargetId).Health;
        Step(world, 100);
        Assert.Equal(initial.Health, world.Combat.Get(full.EntityId).Health);
        Assert.Equal(initial.Health, world.Combat.Get(nearFull.EntityId).Health);
        Assert.Equal(0, world.Combat.Get(dead.EntityId).Health);
        Assert.Equal(10, world.Combat.Get(offline.EntityId).Health);
        Assert.Equal(dummyHealth, world.Combat.Get(world.TrainingTargetId).Health);
        Assert.Empty(world.Combat.HealthRecovered);
        Assert.Empty(world.PersistenceDirty);
    }

    [Fact]
    public void EquipmentRateChangesDoNotLoseFractionAndNegativeRecoveryDoesNotDrainHp()
    {
        var world = World();
        var player = world.AddPlayer(42, new(1), world.CreateInitialCharacter() with { Health = 10 });
        var combat = world.Combat!; var actor = combat.Get(player.EntityId);
        Step(world, 10);
        combat.ApplyEquipment(player.EntityId, (actor.Stats with { HealthRecovery = 2 }, actor.Weapon, actor.AttackInterval));
        Step(world, 10);
        Assert.Equal(12, actor.Health);
        combat.ApplyEquipment(player.EntityId, (actor.Stats with { HealthRecovery = -1 }, actor.Weapon, actor.AttackInterval));
        Step(world, 100);
        Assert.Equal(12, actor.Health);
        combat.ApplyEquipment(player.EntityId, (actor.Stats with { HealthRecovery = .25 }, actor.Weapon, actor.AttackInterval));
        Step(world, 80);
        Assert.Equal(13, actor.Health, 8);
    }

    [Fact]
    public void ReconnectDoesNotHealOfflineAndRemovedActorsDoNotRemainScheduled()
    {
        var world = World();
        world.AddPlayer(42, new(1), world.CreateInitialCharacter() with { Health = 10 });
        Step(world, 20);
        var saved = world.CaptureCharacter(42) with { SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds() };
        world.RemovePlayer(42);
        Step(world, 40);
        Assert.Empty(world.Combat!.HealthRecovered);
        var restored = world.AddPlayer(42, new(2), saved);
        Assert.Equal(saved.Health, world.Combat.Get(restored.EntityId).Health);
        Step(world, 20);
        Assert.Equal(saved.Health + world.Combat.Get(restored.EntityId).Stats.HealthRecovery, world.Combat.Get(restored.EntityId).Health, 8);
    }
}
