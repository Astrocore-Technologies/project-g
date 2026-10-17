using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class NpcActionPhaseTests
{
    private static (ServerWorld World,NetworkEntityId Player,NpcSimulation Npc) Fight(bool bossClaw=false,bool realBoss=false)
    {
        var settings=new NpcOptions {Enabled=true,DefinitionId=bossClaw ? "test_boss" : "test_creature",
            X=-6,Z=0,WindupSeconds=9,DecisionSeconds=1.9f};
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),
            npc:realBoss ? null : Options.Create(settings),boss:realBoss ? Options.Create(new BossOptions {Actor=settings}) : null,
            hitRoll:()=>0,criticalRoll:()=>1);
        var player=world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {X=-7,Z=0});
        var npc=realBoss ? world.Boss! : world.Npc!;
        foreach(var id in new[]{player.EntityId,npc.Id})
        {
            var actor=world.Combat!.Get(id);
            world.Combat.ApplyEquipment(id,(actor.BaseDerivedStats with {AttackSpeedMultiplier=1,MaxHealth=10000,HealthRecovery=0},actor.Weapon with {AttackIntervalSeconds=1},1));
            actor.Health=10000;
        }
        world.Combat!.DamagePermission=(_,target)=>target!=world.TrainingTargetId;
        return(world,player.EntityId,npc);
    }

    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public void WeaponPhasesOwnContactTimeAndCadenceRegardlessOfLegacyWindupAndDecisionRate(bool bossClaw,bool small)
    {
        var (world,player,npc)=Fight(bossClaw);var times=new List<double>();
        world.Combat!.DamageApplied+=(source,target,_)=>{if(source==npc.Id && target==player)times.Add(world.Combat.Time);};
        if(small)for(var i=0;i<48;i++)world.Simulate(.0625f);else world.Simulate(3);
        Assert.Equal(3,times.Count);
        for(var i=0;i<times.Count;i++)Assert.Equal(i+(bossClaw?.6:.5),times[i],10);
        Assert.Equal(bossClaw?"boss_claw":"npc_claw",world.Combat.Get(npc.Id).Weapon.Id);
    }

    [Fact]public void ChangingAttackSpeedPreservesTheStartedNpcSwingAndRescalesTheNextOne()
    {
        var (world,player,npc)=Fight();var times=new List<double>();
        world.Combat!.DamageApplied+=(source,target,_)=>{if(source==npc.Id && target==player)times.Add(world.Combat.Time);};
        world.Simulate(.0625f);var actor=world.Combat.Get(npc.Id);var action=actor.Action!;
        world.Combat.ApplyEquipment(npc.Id,(actor.BaseDerivedStats with {AttackSpeedMultiplier=2},actor.Weapon,.5));
        Assert.Equal(.5,action.PhaseEndsAt);Assert.Equal(1,actor.ReadyAt);
        world.Simulate(1.5f);Assert.Equal(new[]{.5,1.25},times);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void FastNpcBasicsHaveNoFixedTickOrAiDecisionCadenceFloor(bool small)
    {
        var (world,player,npc)=Fight();var actor=world.Combat!.Get(npc.Id);var times=new List<double>();
        world.Combat.ApplyEquipment(npc.Id,(actor.BaseDerivedStats with {AttackSpeedMultiplier=64},actor.Weapon,1d/64));
        world.Combat.DamageApplied+=(source,target,_)=>{if(source==npc.Id && target==player)times.Add(world.Combat.Time);};
        if(small)for(var i=0;i<64;i++)world.Simulate(1f/256);else world.Simulate(.25f);
        Assert.Equal(16,times.Count);
        for(var i=0;i<times.Count;i++)Assert.Equal((i+.5)/64,times[i],12);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ClawsKeepTheQueuedRouteStillUntilRecoveryAndThenAllowMotion(bool bossClaw)
    {
        var (world,_,npc)=Fight(bossClaw);var actor=world.Combat!.Get(npc.Id);
        world.Combat.ApplyEquipment(npc.Id,(actor.BaseDerivedStats,actor.Weapon with {AttackIntervalSeconds=1.25},1.25));
        world.Simulate(.0625f);var action=actor.Action!;var origin=npc.Motion.Position;
        Assert.False(action.CanTurn);Assert.Equal(0,action.MovementMultiplier);
        Assert.True(npc.Motion.TrySetTarget(new(-3,0),0));
        var recoveryAt=bossClaw?.875:.75;
        world.Simulate((float)(recoveryAt-world.Combat.Time));
        Assert.Equal(origin,npc.Motion.Position);Assert.Equal(CombatActionPhase.Recovery,action.Phase);
        Assert.True(action.CanTurn);Assert.Equal(1,action.MovementMultiplier);
        world.Simulate(.125f);Assert.True(npc.Motion.Position.X>origin.X);
    }

    [Fact]public void BossClawsUseTheSameCapabilityInterruptRulesAsOtherWeaponActions()
    {
        var (world,_,npc)=Fight(bossClaw:true,realBoss:true);world.Simulate(.0625f);
        var actor=world.Combat!.Get(npc.Id);var action=actor.Action!;
        Assert.True(world.Combat.ApplyStatusControl(default,npc.Id,0,0,CrowdControlKind.Silence,.25));
        Assert.Same(action,actor.Action);
        Assert.True(world.Combat.ApplyStatusControl(default,npc.Id,0,0,CrowdControlKind.Disarm,.25));
        Assert.Null(actor.Action);Assert.Equal(ActionStopReason.EnemyInterrupt,action.StopReason);
        world.Simulate(.7f);Assert.DoesNotContain(world.Combat.Events,e=>e.AttackerId==npc.Id);
    }
}
