using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CombatPresenceTests
{
    [Fact]
    public void EmptyOrRejectedBasicDoesNotTagAndConfirmedNumericEvasionTagsPveForThirtySeconds()
    {
        long now=100000;var world=World(hitRoll:1);world.PvpClock=()=>now;var player=world.AddPlayer(42,new(1));var combat=world.Combat!;
        Assert.False(world.HasPvp);world.TryQueueAttack(42,new(1,0,-Vector2.UnitX));world.Simulate(.05f);
        Assert.False(world.IsCombatTagged(42));
        var hit=combat.ResolveDirectHit(player.EntityId,world.TrainingTargetId,Vector2.UnitY,10,false);
        Assert.True(hit.Evaded);Assert.Equal(0,hit.Damage);Assert.True(world.IsCombatTagged(42));
        Assert.Equal(30,combat.CombatRemaining(player.EntityId));
        combat.Move(player.EntityId,new(10,10));now+=29999;world.Simulate(.05f);Assert.True(world.IsCombatTagged(42));
        now++;world.Simulate(.05f);Assert.False(world.IsCombatTagged(42));
        Assert.Contains(42,world.PersistenceDirty);
    }

    [Fact]
    public void ParryAndSuccessfulControlTagWhileRejectedControlAndSelfPriceDoNot()
    {
        long now=100000;var world=World();world.PvpClock=()=>now;var target=world.AddPlayer(42,new(1));var source=world.AddPlayer(43,new(2));var combat=world.Combat!;
        combat.DamagePermission=(_,_)=>true;combat.Move(source.EntityId,combat.Get(target.EntityId).Position+Vector2.UnitY);
        combat.QueueDefense(target.EntityId,new(1,DefenseAction.Parry,Vector2.UnitY),0);world.Simulate(.05f);
        var hit=combat.ResolveDirectHit(source.EntityId,target.EntityId,-Vector2.UnitY,10,false,parryable:true);
        Assert.Equal(GuardImpact.Parried,hit.Guard);Assert.True(combat.InCombat(source.EntityId));Assert.True(combat.InCombat(target.EntityId));
        now+=30000;world.Simulate(.05f);Assert.False(combat.InCombat(target.EntityId));
        combat.Get(target.EntityId).CanBeStunned=false;combat.Stun(target.EntityId,1);Assert.False(combat.InCombat(target.EntityId));
        Assert.True(combat.PayHealth(target.EntityId,1));Assert.False(combat.InCombat(target.EntityId));
        Assert.True(combat.ApplyStatusControl(default,target.EntityId,1,1,CrowdControlKind.Root,1));Assert.True(combat.InCombat(target.EntityId));
    }

    [Fact]
    public void OnlyEffectiveSupportToACombatParticipantRefreshesBothParticipants()
    {
        var shield=new EffectDefinition{Id="shield",DurationSeconds=5,Shield=new(){Power=new(){Flat=10}}};
        var world=TimedEffectTests.World(shield);long now=100000;world.PvpClock=()=>now;
        var target=world.AddPlayer(42,new(1));var healer=world.AddPlayer(43,new(2));var combat=world.Combat!;
        combat.SupportPermission=(_,_)=>true;
        combat.Get(target.EntityId).Health=20;combat.Heal(healer.EntityId,target.EntityId,1);Assert.False(combat.InCombat(healer.EntityId));
        combat.DamagePermission=(_,_)=>true;combat.ApplyAbilityDamage(target.EntityId,1,world.TrainingTargetId);
        now+=29000;combat.Heal(healer.EntityId,target.EntityId,1);
        Assert.Equal(30,combat.CombatRemaining(target.EntityId));Assert.Equal(30,combat.CombatRemaining(healer.EntityId));
        now+=29000;combat.SupportPermission=(_,_)=>true;Assert.True(combat.ApplyEffect(healer.EntityId,target.EntityId,"shield"));
        Assert.Equal(30,combat.CombatRemaining(target.EntityId));Assert.Equal(30,combat.CombatRemaining(healer.EntityId));
    }

    [Fact]
    public void EquipmentSkillBarAndRegionTravelWaitForCombatExpiry()
    {
        var world=PvpTests.World();long now=100000;world.PvpClock=()=>now;var player=world.AddPlayer(42,new(1),PvpTests.Initial(world));
        var handle=world.Inventory!.State(player.EntityId,0).Items.Single().Handle;
        world.Combat!.ApplyStatusControl(default,player.EntityId,1,1,CrowdControlKind.Root,.1);
        Assert.True(world.TryQueueInventory(42,new(1,InventoryAction.Unequip,handle)));
        Assert.True(world.TryQueueProgression(42,new(1,ProgressionAction.AssignSlot,0,1,1)));world.Simulate(.05f);
        Assert.Equal(InventoryOutcome.Busy,world.Inventory.Results[player.EntityId].Outcome);
        Assert.Equal(ProgressionOutcome.Busy,world.ProgressionResults[player.EntityId].Outcome);Assert.False(world.ReadyForRegionTravel(42));
        now+=30000;world.Simulate(.2f);Assert.True(world.TryQueueInventory(42,new(2,InventoryAction.Unequip,handle)));world.Simulate(.05f);
        Assert.Equal(InventoryOutcome.Accepted,world.Inventory.Results[player.EntityId].Outcome);
    }

    [Fact]
    public void CombatPresencePersistsAndOfflineTimeOnlyExpiresIt()
    {
        var world=World();long now=100000;world.PvpClock=()=>now;var player=world.AddPlayer(42,new(1));
        world.Combat!.ResolveDirectHit(player.EntityId,world.TrainingTargetId,Vector2.UnitY,1,false);
        var state=CharacterState.Deserialize(world.CaptureCharacter(42).Serialize()) with {SavedAtUnixMilliseconds=DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds()};
        var next=World();next.PvpClock=()=>now;var restored=next.AddPlayer(42,new(1),state);
        Assert.InRange(next.Combat!.CombatRemaining(restored.EntityId),24.8,25);
        Assert.Equal(30,world.Combat.DefenseState(player.EntityId,0).InCombatRemaining);
    }

    [Fact]
    public void DisconnectedBodyContinuesForcedMotionResourceRecoveryAndRemainsTargetableAfterTagEnds()
    {
        var world=World();long now=100000;world.PvpClock=()=>now;
        var player=world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {Health=10,Mana=0});
        var combat=world.Combat!;combat.RestoreDefense(player.EntityId,new(1,10,0,0));
        var start=player.Position;Assert.True(combat.Knockback(player.EntityId,Vector2.UnitX,2,2));
        world.DisconnectPvp(42);world.RebindConnection(42,-1,player.PlayerId);
        for(var i=0;i<22;i++)world.Simulate(.05f);
        Assert.Equal(start.X+2,player.Position.X,5);Assert.True(combat.Get(player.EntityId).Health>10);
        Assert.True(world.Abilities!.Mana(player.EntityId)>0);Assert.True(combat.DefenseState(player.EntityId,world.Tick).Stamina>10);
        now+=30001;world.SetLoaded(-1,false);Assert.True(combat.Get(player.EntityId).Active);
        combat.DamagePermission=(_,_)=>true;Assert.True(combat.ApplyAbilityDamage(player.EntityId,1,world.TrainingTargetId)>0);
        Assert.True(combat.Get(player.EntityId).DetachedBody);
    }

    private static ServerWorld World(double hitRoll=0)=>new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
        catalog:ContentCatalogTests.Load(),hitRoll:()=>hitRoll,criticalRoll:()=>1);
}
