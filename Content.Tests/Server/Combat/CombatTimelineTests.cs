using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CombatTimelineTests
{
    private sealed class Pair
    {
        internal readonly ServerWorld World;
        internal readonly ServerPlayer Caster,Striker;
        internal CombatSimulation Combat=>World.Combat!;
        internal AbilitySimulation Abilities=>World.Abilities!;
        internal Pair(double preparation=.2,bool channel=false,double health=1)
        {
            var data=BasicActionPhaseTests.Document();
            if(channel)
            {
                var channelData=ChannelingTests.Document();
                data["abilities"]![1]=channelData["abilities"]![1]!.DeepClone();
                data["abilities"]![1]!["channel"]!["intervalSeconds"]=.2;
            }
            else
            {
                var skill=data["abilities"]![1]!;
                skill["action"]!["preparation"]!["seconds"]=preparation;
                skill["action"]!["preparation"]!["speed"]="None";
                skill["power"]=40;skill["magicAttackScale"]=0;skill["radius"]=1;
            }
            var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,hitRoll:()=>0,criticalRoll:()=>1);
            Caster=World.AddPlayer(42,new(1),World.CreateInitialCharacter() with {X=-7,Z=1,Health=health});
            Striker=World.AddPlayer(43,new(2),World.CreateInitialCharacter() with {X=-7,Z=0,Health=health});
            Combat.DamagePermission=(_,target)=>target!=World.TrainingTargetId;
            foreach(var id in new[]{Caster.EntityId,Striker.EntityId})
            {
                var actor=Combat.Get(id);
                Combat.ApplyEquipment(id,(actor.BaseDerivedStats with {AttackSpeedMultiplier=1,HealthRecovery=0,ManaRecovery=0},actor.Weapon,1));
            }
        }
        internal void Start(bool reverse=false)
        {
            if(reverse)Basic();
            Assert.True(World.TryQueueAbility(42,new(1,World.Tick,2,Striker.Position),0));
            if(!reverse)Basic();
            World.Simulate(.05f);
        }
        private void Basic()=>Assert.True(World.TryQueueAttack(43,new(1,World.Tick,Vector2.UnitY)));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void CastAndBasicShareTheSameContactBarrierAtTheirExactPreparationEnd(bool reverse)
    {
        var f=new Pair();f.Start(reverse);Assert.Empty(f.Abilities.Hits);Assert.Empty(f.Combat.Events);
        f.World.Simulate(.6f);
        Assert.Equal(1,Assert.Single(f.Abilities.Hits).Damage);Assert.Equal(1,Assert.Single(f.Combat.Events).Damage);
        Assert.All(new[]{f.Caster.EntityId,f.Striker.EntityId},id=>Assert.True(f.Combat.Get(id).LifeDead));
    }

    [Fact]public void AnEarlierCastInTheSameWorldTickKillsAndCancelsTheLaterBasic()
    {
        var f=new Pair(.1);f.Start();f.World.Simulate(.6f);
        Assert.Single(f.Abilities.Hits);Assert.Empty(f.Combat.Events);
        Assert.Equal(1,f.Combat.Get(f.Caster.EntityId).Health);Assert.True(f.Combat.Get(f.Striker.EntityId).LifeDead);
    }

    [Fact]public void AChannelPulseAndBasicCanTradeWithoutChargingTheDeadChannelForTheRestOfTheTick()
    {
        var f=new Pair(channel:true);f.Start();f.World.Simulate(.6f);
        Assert.Single(f.Abilities.Hits);Assert.Single(f.Combat.Events);
        Assert.True(f.Combat.Get(f.Caster.EntityId).LifeDead);Assert.True(f.Combat.Get(f.Striker.EntityId).LifeDead);
        Assert.Equal(97.4,f.Abilities.Mana(f.Caster.EntityId),7);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void AContactUsesTheMovingTargetsPositionAtItsBoundaryInsideALargeTick()
    {
        var f=new Pair(health:100);var range=(float)f.Combat.Get(f.Striker.EntityId).Weapon.Range;
        f.Caster.Motion.Reset(new(-7,range-.8f),new(-7,range-.8f));f.Combat.Move(f.Caster.EntityId,f.Caster.Position);
        Assert.True(f.World.TryQueueAttack(43,new(1,0,Vector2.UnitY)));f.World.Simulate(.05f);
        Assert.True(f.World.TryApplyMove(42,new(1,f.World.Tick,new(-7,range+3))));
        f.World.Simulate(.6f);
        Assert.Equal(f.Caster.EntityId,Assert.Single(f.Combat.Events).TargetId);
        Assert.True(Vector2.Distance(f.Striker.Position,f.Caster.Position)>range);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void SubTickBasicCadenceHasNoTickFloorAndNoDriftBetweenLargeAndSmallSteps(bool phased)
    {
        (double[] Times,double Health) Run(bool small)
        {
            var f=new BasicActionPhaseTests.Fight();var times=new List<double>();
            var weapon=phased?f.Actor.Weapon:f.Actor.Weapon with {BasicAttack=null};
            f.Combat.ApplyEquipment(f.Player,(f.Actor.BaseDerivedStats with {AttackSpeedMultiplier=64},weapon,1d/64));
            Assert.Equal(1d/64,f.Actor.AttackInterval,10);
            f.Combat.DamageApplied+=(_,_,_)=>times.Add(f.Combat.Time);
            f.Begin(BasicAttackMode.BeginContinuous);
            if(small)for(var i=0;i<16;i++)f.Step(1f/64);else f.Step(.25f);
            return(times.ToArray(),f.Combat.Get(f.Target).Health);
        }
        var large=Run(false);var small=Run(true);
        Assert.Equal(phased?16:17,large.Times.Length);Assert.Equal(large.Times.Length,small.Times.Length);
        for(var i=0;i<large.Times.Length;i++)Assert.Equal(large.Times[i],small.Times[i],7);
        Assert.Equal(large.Health,small.Health,7);
    }

    [Fact]public void PeriodicEventsAreImportedOnceWhenTheyInterleaveWithSeveralChannelContacts()
    {
        var data=ChannelingTests.Document();data["effects"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"timeline_dot","durationSeconds":0.4,"disposition":"Harmful",
             "periodic":{"kind":"Damage","intervalSeconds":0.1,"power":{"flat":1}}}
            """));
        var f=new ChannelingTests.Fight(data);Assert.True(f.Combat.ApplyEffect(f.Id,f.World.TrainingTargetId,"timeline_dot"));
        f.Cast();f.World.Simulate(.4f);
        Assert.Equal(4,f.Abilities.DirectHits.Count);Assert.Equal(8,f.Abilities.Hits.Count);
        Assert.Equal(4,f.Combat.EffectEvents.Count(e=>e.Kind==CombatEffectEventKind.PeriodicDamage));
    }

    [Fact]public void ThePublicCombatAbilityPairAdvancesOneFrameAndAbilityOnlyCallersShareItsClock()
    {
        var f=new ChannelingTests.Fight();f.Cast();var time=f.Combat.Time;
        f.Abilities.Simulate(.05f,f.World.Tick);Assert.Equal(time,f.Combat.Time);
        f.Abilities.Simulate(.05f,f.World.Tick+1);Assert.Equal(time+.05f,f.Combat.Time,10);
        Assert.Equal(97.85,f.Mana,7);
    }

    [Fact]public void ARecoveryLockDoesNotCreditStaminaForTheLockedIntervalEndingAtThisBoundary()
    {
        var f=new BasicActionPhaseTests.Fight();f.Combat.SpendStamina(f.Player,50);f.Combat.SetRecovery(f.Player,2);
        f.Step(2.05f);
        var expected=50+f.Combat.Get(f.Player).Stats.StaminaRecovery*(f.Combat.Time-2);
        Assert.Equal(expected,f.Combat.AvailableStamina(f.Player),6);
    }

    [Fact]public void AReleasedProjectilesActionRecoveryEndsAtItsOwnBoundaryWhileTheProjectileKeepsFlying()
    {
        var data=ChannelingTests.Document();var skill=data["abilities"]![0]!;
        skill["action"]!["form"]="Instant";skill["action"]!["preparation"]!["seconds"]=0;
        skill["action"]!["recovery"]=new JsonObject{["seconds"]=.125,["speed"]="None",["cancel"]="None"};
        var f=new ChannelingTests.Fight(data);
        Assert.True(f.World.TryQueueAbility(42,new(1,0,1,Vector2.UnitY),0));f.Step();
        var ready=f.Combat.Time+.125;var projectile=Assert.Single(f.Abilities.ActiveStates).EffectId;
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,2,f.Combat.Get(f.World.TrainingTargetId).Position),0));f.Step();
        Assert.Equal(AbilityOutcome.Buffered,f.Abilities.Results[f.Id].Outcome);f.World.Simulate(.1f);
        Assert.Equal(AbilityOutcome.Accepted,f.Abilities.Results[f.Id].Outcome);
        Assert.Equal(ready,f.Actor.Action!.PhaseStartedAt,10);
        Assert.Contains(f.Abilities.ActiveStates,s=>s.EffectId==projectile && s.Phase==AbilityPhase.Flying);
    }
}
