using System.Numerics;
using System.Text;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ProjectileTimelineTests
{
    private sealed class Fight
    {
        internal readonly ServerWorld World;
        internal readonly ServerPlayer Source,Target;
        internal readonly List<(double At,NetworkEntityId Source,NetworkEntityId Target)> Contacts=[];
        internal CombatSimulation Combat=>World.Combat!;
        internal AbilitySimulation Abilities=>World.Abilities!;
        internal Fight(float targetX=-5.5f,float targetZ=0,double health=1,bool wall=false,double recovery=0,string cancel="None")
        {
            var data=BasicActionPhaseTests.Document();var skill=data["abilities"]![0]!;
            // Binary-exact contact distance for time/barrier tests; production body sizes have their own geometry tests.
            data["creatures"]![0]!["hurtbox"]!["radius"]=.25;
            foreach(var weapon in data["weapons"]!.AsArray())
                if(weapon!["id"]!.GetValue<string>() is "training_sword" or "unarmed")weapon["attackIntervalSeconds"]=1.25;
            skill["action"]!["form"]="Instant";skill["action"]!["preparation"]!["seconds"]=0;
            skill["action"]!["preparation"]!["speed"]="None";skill["action"]!["active"]=new System.Text.Json.Nodes.JsonObject();
            skill["action"]!["recovery"]=new System.Text.Json.Nodes.JsonObject{["seconds"]=recovery,["speed"]="None",["cancel"]=cancel};
            skill["power"]=40;skill["magicAttackScale"]=0;skill["speed"]=4;skill["range"]=8;skill["radius"]=.25;
            var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
            World=new(Options.Create(new MovementOptions{Speed=4}),Options.Create(new InterestOptions()),
                Options.Create(new NavigationOptions{AgentRadius=.25f,BlockedAreas=wall?[new(){X=12,Z=14,Width=1,Height=3}]:[]}),catalog,hitRoll:()=>0,criticalRoll:()=>1);
            Source=World.AddPlayer(42,new(1),World.CreateInitialCharacter() with {X=-7,Z=0,Health=health});
            Target=World.AddPlayer(43,new(2),World.CreateInitialCharacter() with {X=targetX,Z=targetZ,Health=health});
            Combat.DamagePermission=(_,target)=>target!=World.TrainingTargetId;
            foreach(var id in new[]{Source.EntityId,Target.EntityId})
            {
                var actor=Combat.Get(id);
                Combat.ApplyEquipment(id,(actor.BaseDerivedStats with {AttackSpeedMultiplier=1,HealthRecovery=0},actor.Weapon,1.25));
                Assert.Equal(1.25,actor.AttackInterval,12);
            }
            Combat.DamageApplied+=(source,target,_)=>Contacts.Add((Combat.Time,source,target));
        }
        internal void Cast(int connection=42,Vector2? direction=null)=>Assert.True(World.TryQueueAbility(connection,new(1,World.Tick,1,direction??Vector2.UnitX),0));
        internal void Start()=>World.Simulate(.05f);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void AProjectileAndBasicAtTheSameMomentBothLandBeforeEitherDeath(bool reverse)
    {
        var f=new Fight();
        if(reverse)Assert.True(f.World.TryQueueAttack(43,new(1,0,-Vector2.UnitX)));
        f.Cast();if(!reverse)Assert.True(f.World.TryQueueAttack(43,new(1,0,-Vector2.UnitX)));
        f.Start();var time=f.Combat.Time+.25;f.World.Simulate(.6f);
        Assert.Single(f.Combat.Events);Assert.Single(f.Abilities.Hits);Assert.Equal(2,f.Contacts.Count);
        Assert.All(f.Contacts,c=>Assert.Equal(time,c.At,12));
        Assert.True(f.Combat.Get(f.Source.EntityId).LifeDead);Assert.True(f.Combat.Get(f.Target.EntityId).LifeDead);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void OpposingProjectilesCrossEachOtherAndCanKillBothSources(bool reverse)
    {
        var f=new Fight();if(reverse)f.Cast(43,-Vector2.UnitX);f.Cast();if(!reverse)f.Cast(43,-Vector2.UnitX);
        f.Start();f.World.Simulate(.6f);
        Assert.Equal(2,f.Abilities.Hits.Count);Assert.Equal(2,f.Contacts.Count);Assert.Equal(f.Contacts[0].At,f.Contacts[1].At);
        Assert.True(f.Combat.Get(f.Source.EntityId).LifeDead);Assert.True(f.Combat.Get(f.Target.EntityId).LifeDead);
    }
    [Fact]public void AnEarlierProjectileCancelsTheLaterBasicInsteadOfTradingAtTheTickEnd()
    {
        var f=new Fight(targetX:-6);f.Cast();Assert.True(f.World.TryQueueAttack(43,new(1,0,-Vector2.UnitX)));
        f.Start();var at=f.Combat.Time+.125;f.World.Simulate(.6f);
        Assert.Single(f.Abilities.Hits);Assert.Empty(f.Combat.Events);Assert.Equal(at,Assert.Single(f.Contacts).At,12);
        Assert.Equal(1,f.Combat.Get(f.Source.EntityId).Health);Assert.True(f.Combat.Get(f.Target.EntityId).LifeDead);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void ContactTimeIsIndependentOfTheWorldStepForStationaryAndCrossingTargets(bool moving)
    {
        double Run(bool small)
        {
            var f=new Fight(targetZ:moving?-1:0,health:100);f.Cast();f.Start();var start=f.Combat.Time;
            if(moving)Assert.True(f.World.TryApplyMove(43,new(1,f.World.Tick,new(-5.5f,3))));
            var hits=0;
            if(small)for(var i=0;i<12;i++){f.World.Simulate(.05f);hits+=f.Abilities.Hits.Count;}
            else {f.World.Simulate(.6f);hits+=f.Abilities.Hits.Count;}
            Assert.Equal(1,hits);
            return Assert.Single(f.Contacts).At-start;
        }
        // The last small step no longer contains the earlier hit, so contact history is the authority.
        var large=Run(false);var small=Run(true);Assert.Equal(.25,large,6);Assert.Equal(large,small,6);
    }
    [Fact]public void ATargetMovingAtProjectileSpeedCannotBeHitUsingItsEndOfTickPose()
    {
        var f=new Fight(health:100);f.Cast();f.Start();Assert.True(f.World.TryApplyMove(43,new(1,f.World.Tick,new(5,0))));
        f.World.Simulate(2.1f);Assert.Empty(f.Contacts);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void AStationaryDiagonalTargetHasOneAbsoluteArrivalTimeAcrossDifferentStepSizes()
    {
        double Run(bool small)
        {
            var f=new Fight(targetZ:1.5f,health:100);f.Cast(direction:Vector2.Normalize(Vector2.One));f.Start();
            if(small)for(var i=0;i<32;i++)f.World.Simulate(1f/64);else f.World.Simulate(.5f);
            return Assert.Single(f.Contacts).At;
        }
        Assert.Equal(Run(false),Run(true),12);
    }

    [Fact]public void AnInitialOverlapResolvesAtReleaseWithoutWaitingForAnotherTick()
    {
        var f=new Fight(targetX:-6.75f,health:100);f.Cast();f.Start();
        Assert.Equal(f.Combat.Time,Assert.Single(f.Contacts).At);Assert.Single(f.Abilities.Hits);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void AContactExactlyAtMaximumRangeIsNotDiscardedWithTheProjectile()
    {
        var f=new Fight(targetX:1.5f,health:100);f.Cast();f.Start();var arrival=f.Combat.Time+2;
        f.World.Simulate(2.1f);Assert.Equal(arrival,Assert.Single(f.Contacts).At,12);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void TheFirstWallBoundaryStopsFlightBeforeTheTargetBehindIt()
    {
        var f=new Fight(targetX:-1,health:100,wall:true);f.Cast();f.Start();
        f.World.Simulate(.9f);Assert.Single(f.Abilities.ActiveStates);Assert.Empty(f.Contacts);
        f.World.Simulate(.04f);Assert.Empty(f.Abilities.ActiveStates);Assert.Empty(f.Contacts);
    }

    [Fact]public void AProjectilePassingBelowAnAirborneTargetDoesNotHitItsLaterGroundedPose()
    {
        var f=new Fight(health:100);Assert.True(f.Combat.Knockup(f.Target.EntityId,.75f,2,1));f.Cast();f.Start();
        f.World.Simulate(.9f);Assert.Empty(f.Contacts);Assert.Equal(CombatControlPhase.KnockedDown,f.Combat.Get(f.Target.EntityId).Control);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ACloseRangeHitEndsTheEffectWithoutLosingTheIndependentActionRecovery(bool bufferedBasic)
    {
        var f=new Fight(targetX:-6.75f,health:100,recovery:.25);f.Cast();f.Start();
        var action=f.Combat.Get(f.Source.EntityId).Action!;var ready=f.Combat.Time+.25;
        Assert.Empty(f.Abilities.ActiveStates);Assert.False(action.Finished);f.World.Simulate(.2f);Assert.False(action.Finished);
        if(bufferedBasic)
        {
            Assert.True(f.World.TryQueueAttack(42,new(1,f.World.Tick,Vector2.UnitX)));f.World.Simulate(.025f);
            Assert.Equal(AttackOutcome.Buffered,f.Combat.Results[f.Source.EntityId].Outcome);f.World.Simulate(.075f);
        }
        else f.World.Simulate(.1f);
        Assert.True(action.Finished);
        if(bufferedBasic)
        {
            var next=f.Combat.Get(f.Source.EntityId).Action!;
            Assert.Equal(Content.Shared.Combat.CombatActionCategory.BasicAttack,next.Definition.Category);Assert.Equal(ready,next.PhaseStartedAt,12);
        }
        else Assert.Null(f.Combat.Get(f.Source.EntityId).Action);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void TheRemainingRecoveryCanStillBeCancelledOrEndedByDeathAfterItsProjectileDisappears(bool death)
    {
        var f=new Fight(targetX:-6.75f,health:100,recovery:.25,cancel:"Voluntary");f.Cast();f.Start();
        var action=f.Combat.Get(f.Source.EntityId).Action!;var mana=f.Abilities.Mana(f.Source.EntityId);
        Assert.Empty(f.Abilities.ActiveStates);
        if(death)Assert.True(f.Combat.ApplyAbilityDamage(f.Source.EntityId,100000,f.Target.EntityId)>0);
        else Assert.True(f.Abilities.CancelAction(f.Source.EntityId,1,action.ExecutionId));
        Assert.True(action.Finished);Assert.Null(f.Combat.Get(f.Source.EntityId).Action);
        Assert.Equal(death?ActionStopReason.Death:ActionStopReason.Voluntary,action.StopReason);Assert.Equal(mana,f.Abilities.Mana(f.Source.EntityId));
    }
}
