using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class StatusControlTests
{
    [Fact]
    public void RootStopsMovementAndDodgeWithoutInterruptingMagicCast()
    {
        var world=World(); var player=world.AddPlayer(42,new(1)); var combat=world.Combat!;
        Assert.True(world.TryQueueAbility(42,new(1,0,1,Vector2.UnitX),0)); world.Simulate(.05f);
        Assert.True(combat.ApplyStatusControl(default,player.EntityId,1,1,CrowdControlKind.Root,2));
        Assert.True(combat.Get(player.EntityId).IsCasting);
        Assert.False(world.TryApplyMove(42,new(1,0,player.Position+Vector2.UnitX)));
        Assert.True(world.TryQueueAbility(42,new(2,0,3,Vector2.UnitX),0)); world.Simulate(.05f);
        Assert.Equal(AbilityOutcome.InvalidState,world.Abilities!.Results[player.EntityId].Outcome);
        Assert.True(combat.Get(player.EntityId).IsCasting);
    }

    [Fact]
    public void SilenceInterruptsMagicAtHalfCostAndDisarmLeavesMagicAvailable()
    {
        var world=World(); var player=world.AddPlayer(42,new(1)); var combat=world.Combat!;
        Assert.True(world.TryQueueAbility(42,new(1,0,1,Vector2.UnitX),0)); world.Simulate(.05f);
        combat.ApplyStatusControl(default,player.EntityId,1,1,CrowdControlKind.Silence,2);
        Assert.False(combat.Get(player.EntityId).IsCasting);
        Assert.Equal(97.5,world.Abilities!.Loadout(player.EntityId,world.Tick).Mana);
        combat.RemoveControl(player.EntityId,CrowdControlKind.Silence);
        combat.ApplyStatusControl(default,player.EntityId,2,2,CrowdControlKind.Disarm,2);
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitX))); world.Simulate(.05f);
        Assert.Equal(AttackOutcome.InvalidState,combat.Results[player.EntityId].Outcome);
        Assert.True(world.TryQueueAbility(42,new(2,0,2,player.Position),0)); world.Simulate(.05f);
        Assert.Equal(AbilityOutcome.Accepted,world.Abilities.Results[player.EntityId].Outcome);
    }

    [Fact]
    public void DamageWakesSleepWithoutRefundingBudgetAndDoesNotRemoveStun()
    {
        var world=World(); var player=world.AddPlayer(42,new(1)); var combat=world.Combat!;
        combat.DamagePermission=(_,_)=>true;
        combat.ApplyStatusControl(default,player.EntityId,1,1,CrowdControlKind.Sleep,2);
        combat.ApplyAbilityDamage(player.EntityId,1,world.TrainingTargetId);
        Assert.False(combat.IsStunned(player.EntityId));
        Assert.Equal(2,combat.Get(player.EntityId).Controls.Ledger.Snapshot(ControlCategory.Hard,combat.Time).Used);
        combat.Stun(player.EntityId,2);
        combat.ApplyAbilityDamage(player.EntityId,1,world.TrainingTargetId);
        Assert.True(combat.IsStunned(player.EntityId));
    }

    [Fact]
    public void SlowStacksToPositiveFloorAndBlindChangesOnlyNumericHitCheck()
    {
        var world=World(hitRoll:.5); var player=world.AddPlayer(42,new(1)); var combat=world.Combat!;
        combat.Slow(player.EntityId,.8f,1,definition:1);
        combat.Slow(player.EntityId,.8f,1,definition:2);
        Assert.Equal(.1,combat.OrdinaryMovement(player.EntityId),6);
        Assert.False(combat.IsActionLocked(player.EntityId));
        Assert.False(combat.ResolveDirectHit(player.EntityId,world.TrainingTargetId,Vector2.UnitY,1,false).Evaded);
        Assert.True(combat.ApplyStatusControl(default,player.EntityId,3,3,CrowdControlKind.Blind,.1,500));
        Assert.True(combat.ResolveDirectHit(player.EntityId,world.TrainingTargetId,Vector2.UnitY,1,false).Evaded);
    }

    [Fact]
    public void PlayerTauntPenalizesOtherTargetsAndNpcTauntUsesDurationBudget()
    {
        var world=World(npc:true); var owner=world.AddPlayer(42,new(1)); var provoker=world.AddPlayer(43,new(2));
        var combat=world.Combat!; combat.DamagePermission=(_,_)=>true;
        Assert.True(combat.ApplyStatusControl(provoker.EntityId,owner.EntityId,1,1,CrowdControlKind.Taunt,3,.3));
        var normal=combat.ResolveDirectHit(owner.EntityId,provoker.EntityId,Vector2.UnitY,10,false);
        var penalized=combat.ResolveDirectHit(owner.EntityId,world.TrainingTargetId,Vector2.UnitY,10,false);
        var baseline=combat.ResolveDirectHit(provoker.EntityId,world.TrainingTargetId,Vector2.UnitY,10,false);
        Assert.True(normal.Damage>0); Assert.Equal(baseline.Damage*.7,penalized.Damage,8);
        Assert.True(combat.ApplyStatusControl(provoker.EntityId,world.Npc!.Id,2,2,CrowdControlKind.Taunt,3,.3));
        Assert.Equal(2,combat.Get(world.Npc.Id).Controls.LastEnd(CrowdControlKind.Taunt)-combat.Time,8);
        for(var i=0;i<6;i++) world.Simulate(.05f);
        Assert.Equal(provoker.EntityId,world.Npc.TargetId);
    }

    [Fact]
    public void PullAndKnockbackUseMovementBudgetAndKeepTheirSpecifiedSpeed()
    {
        var world=World(); var player=world.AddPlayer(42,new(1)); var combat=world.Combat!;
        var start=player.Position;
        Assert.True(combat.Pull(player.EntityId,start+new Vector2(3,0),2,2));
        Assert.Equal(1,combat.Get(player.EntityId).Controls.Ledger.Snapshot(ControlCategory.Movement,combat.Time).Used,6);
        for(var i=0;i<22;i++) world.Simulate(.05f);
        Assert.Equal(start.X+2,player.Position.X,4);
        Assert.True(combat.Knockback(player.EntityId,-Vector2.UnitX,2,2));
        Assert.Equal(2,player.Motion.DashSpeed);
        Assert.InRange(combat.Get(player.EntityId).ControlUntil-combat.Time,.83,.84);
    }

    [Fact]
    public void FreezeBlocksDefenseAndCleansePreservesSpentBudget()
    {
        var world=World(); var player=world.AddPlayer(42,new(1)); var combat=world.Combat!;
        Assert.True(combat.ApplyStatusControl(default,player.EntityId,1,1,CrowdControlKind.Freeze,2));
        Assert.True(world.TryQueueDefense(42,new(1,DefenseAction.Block,Vector2.UnitY))); world.Simulate(.05f);
        Assert.Equal(DefenseOutcome.InvalidState,combat.DefenseState(player.EntityId,world.Tick).Outcome);
        Assert.True(combat.RemoveControl(player.EntityId,CrowdControlKind.Freeze)); Assert.False(combat.IsStunned(player.EntityId));
        Assert.Equal(2,combat.Get(player.EntityId).Controls.Ledger.Snapshot(ControlCategory.Hard,combat.Time).Used);
    }

    [Fact]
    public void FearMovesRealPlayerAwayOnNavigationRejectsManualActionsAndStopsAtExpiry()
    {
        var world=World(); var player=world.AddPlayer(42,new(1));
        var source=world.AddPlayer(43,new(2),world.CreateInitialCharacter() with {X=-8});
        var combat=world.Combat!; combat.DamagePermission=(_,_)=>true; var start=player.Position;
        Assert.True(combat.ApplyStatusControl(source.EntityId,player.EntityId,1,1,CrowdControlKind.Fear,1));
        Assert.False(world.TryApplyMove(42,new(1,0,start-Vector2.UnitX)));
        Assert.True(world.TryQueueDefense(42,new(1,DefenseAction.Parry,Vector2.UnitY))); world.Simulate(.05f);
        Assert.Equal(DefenseOutcome.InvalidState,combat.DefenseState(player.EntityId,world.Tick).Outcome);
        for(var i=0;i<24;i++) world.Simulate(.05f);
        Assert.True(player.Position.X>start.X+1);
        Assert.False(player.Motion.IsMoving); Assert.True(world.Navigation.IsOnSurface(player.Foot));
        Assert.False(combat.IsFleeing(player.EntityId));
    }

    private static ServerWorld World(bool npc=false,double hitRoll=0)=>new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
        catalog:ContentCatalogTests.Load(),npc:Options.Create(new NpcOptions {Enabled=npc,X=-4,Z=0}),hitRoll:()=>hitRoll,criticalRoll:()=>1);
}
