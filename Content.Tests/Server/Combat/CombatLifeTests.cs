using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;
namespace Content.Tests.Server.Combat;

public sealed class CombatLifeTests
{
    [Fact]public void PveDeathStopsCastPersistsWithoutPkAndRespawnsOnlyAfterSixtySeconds()
    {
        var world=CharacterPersistenceTests.World();long now=100000;world.PvpClock=()=>now;
        var player=world.AddPlayer(42,new(1));var combat=world.Combat!;var id=player.EntityId;Assert.False(world.HasPvp);
        Assert.True(world.TryQueueAbility(42,new(1,world.Tick,1,System.Numerics.Vector2.UnitY),0));world.Simulate(.05f);Assert.True(combat.Get(id).IsCasting);
        Assert.True(combat.PayHealth(id,combat.Get(id).Health,true));world.Simulate(.05f);
        var life=combat.LifeState(id,world.Tick);Assert.True(life.Dead);Assert.Equal(1UL,life.DeathId);Assert.Equal(60,life.CorpseRemaining);Assert.False(combat.Get(id).IsCasting);
        Assert.Contains(42,world.PersistenceDirty);Assert.True(world.TryQueueRespawn(42,new(1,life.DeathId)));world.Simulate(.05f);
        Assert.Equal(RespawnOutcome.NotReady,world.RespawnResults[id].Outcome);
        now+=59999;Assert.True(combat.CanResurrect(id,life.DeathId));now++;world.Simulate(.05f);
        Assert.False(combat.CanResurrect(id,life.DeathId));Assert.Equal(0,combat.LifeState(id,world.Tick).CorpseRemaining);
        Assert.True(world.TryQueueRespawn(42,new(2,life.DeathId)));world.Simulate(.05f);
        Assert.Equal(RespawnOutcome.Accepted,world.RespawnResults[id].Outcome);Assert.Equal(combat.Get(id).Stats.MaxHealth*.5,combat.Get(id).Health);
        Assert.False(combat.LifeState(id,world.Tick).Dead);Assert.False(world.TryQueueRespawn(42,new(2,life.DeathId)));
        Assert.Equal(RespawnOutcome.AlreadyProcessed,world.RespawnResults[id].Outcome);
    }
    [Fact]public void ResurrectionPreservesResourcesAndRejectsStaleDeathsOrExpiredBodies()
    {
        var world=CharacterPersistenceTests.World();long now=100000;world.PvpClock=()=>now;
        var player=world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {Mana=7});var id=player.EntityId;var combat=world.Combat!;
        combat.RestoreDefense(id,new(1,13,.5,1));Assert.True(combat.PayHealth(id,combat.Get(id).Health,true));
        var cooldowns=world.Abilities!.CaptureCooldowns(id);var defense=combat.CaptureDefense(id);
        Assert.False(combat.Resurrect(default,id,99,20));Assert.True(combat.Resurrect(default,id,1,20));
        Assert.Equal(20,combat.Get(id).Health);Assert.Equal(7,world.Abilities.Mana(id));Assert.Equal(defense,combat.CaptureDefense(id));Assert.Equal(cooldowns,world.Abilities.CaptureCooldowns(id));
        Assert.True(combat.PayHealth(id,20,true));Assert.Equal(2UL,combat.LifeState(id,world.Tick).DeathId);
        Assert.False(combat.Resurrect(default,id,1,20));now+=60000;Assert.False(combat.Resurrect(default,id,2,20));
        Assert.Equal(0,combat.Get(id).Health);
    }
    [Fact]public void OfflineDeathTimerIsNotRestartedOrAutomaticallyRespawned()
    {
        var world=CharacterPersistenceTests.World();var player=world.AddPlayer(42,new(1));var combat=world.Combat!;
        combat.PayHealth(player.EntityId,combat.Get(player.EntityId).Health,true);
        var state=world.CaptureCharacter(42) with {SavedAtUnixMilliseconds=DateTimeOffset.UtcNow.AddSeconds(-70).ToUnixTimeMilliseconds()};
        var frozen=Content.Server.Regions.FrozenCharacterState.Capture(state).Restore();world.RemovePlayer(42);
        var restored=world.AddPlayer(42,new(2),frozen);var life=combat.LifeState(restored.EntityId,world.Tick);
        Assert.True(life.Dead);Assert.Equal(1UL,life.DeathId);Assert.Equal(0,life.CorpseRemaining);Assert.Equal(0,life.RespawnRemaining);Assert.Equal(0,combat.Get(restored.EntityId).Health);
    }
    [Fact]public void InvalidSavedLifeFailsClosed()
    {
        Assert.Throws<InvalidDataException>(()=>new SavedCombatLife(1,0,true,1,1).Validate());
        Assert.Throws<InvalidDataException>(()=>new SavedCombatLife(1,1,false,1,0).Validate());
        Assert.Throws<InvalidDataException>(()=>new SavedCombatLife(1,1,true,double.NaN,1).Validate());
        var state=CharacterPersistenceTests.World().CreateInitialCharacter() with {Life=new(1,1,true,1,1)};
        Assert.Throws<InvalidDataException>(()=>state.Serialize());
    }
}
