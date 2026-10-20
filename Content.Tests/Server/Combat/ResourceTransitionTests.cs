using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Shared.Combat;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ResourceTransitionTests
{
    [Theory]
    [InlineData(400,1000,500,200)]
    [InlineData(200,500,1000,400)]
    [InlineData(0,0,100,0)]
    [InlineData(50,100,-20,0)]
    [InlineData(0,100,200,0)]
    [InlineData(double.MaxValue,double.MaxValue,double.MaxValue,double.MaxValue)]
    public void CapacityTransitionPreservesFractionAndPhysicalBounds(double current,double oldMax,double newMax,double expected)
        =>Assert.Equal(expected,ResourceTransition.Amount(current,oldMax,newMax));

    [Fact] public void EffectAndEquipmentChangesScaleOnceWithoutDamageOrCooldownReset()
    {
        var f=new ChannelingTests.Fight(CommonResourceTests.Document());
        var before=f.Actor.Stats;f.Actor.Health=before.MaxHealth*.4;
        f.Combat.DrainMana!(f.Id,before.MaxMana*.6);f.Combat.SpendStamina(f.Id,60);
        f.Actor.ReadyAt=20;var serial=f.Combat.DamageSerial(f.Id);
        Assert.True(f.Combat.ApplyEffect(f.Id,f.Id,"resource_capacity"));
        Assert.Equal(f.Actor.Stats.MaxMana*.4,f.Mana,10);Assert.Equal(80,f.Actor.Resources.Stamina);
        f.Combat.ApplyEquipment(f.Id,(f.Actor.BaseDerivedStats with {MaxHealth=before.MaxHealth/2,MaxMana=before.MaxMana/2},f.Actor.Weapon,f.Actor.AttackInterval));
        // Inventory also replaces the AbilityActor; that must not apply a second transition.
        f.Abilities.ApplyEquipment(f.Id,f.Abilities.PrepareEquipment(f.Id,f.Actor.Stats));
        Assert.Equal(f.Actor.Stats.MaxHealth*.4,f.Actor.Health,10);
        Assert.Equal(f.Actor.Stats.MaxMana*.4,f.Mana,10);Assert.Equal(80,f.Actor.Resources.Stamina);
        Assert.Equal(20,f.Actor.ReadyAt);Assert.Equal(serial,f.Combat.DamageSerial(f.Id));
        f.Actor.Health=0;
        f.Combat.ApplyEquipment(f.Id,(before,f.Actor.Weapon,f.Actor.AttackInterval));Assert.Equal(0,f.Actor.Health);
    }

    [Fact] public void ZeroCapacityBecomesEmptyAndCanThenRecover()
    {
        var f=new ChannelingTests.Fight();var before=f.Actor.Stats;
        f.Combat.ApplyEquipment(f.Id,(before with {MaxMana=0,MaxStamina=0},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Combat.ApplyEquipment(f.Id,(before with {ManaRecovery=2,StaminaRecovery=3},f.Actor.Weapon,f.Actor.AttackInterval));
        Assert.Equal(0,f.Mana);Assert.Equal(0,f.Actor.Resources.Stamina);
        f.Combat.Simulate(.25f,1);Assert.Equal(.5,f.Mana);Assert.Equal(.75,f.Actor.Resources.Stamina);
    }

    [Fact] public void RefilledStaminaWakesNegativeFlowAfterSleepingAtZero()
    {
        var f=new ChannelingTests.Fight();
        f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {StaminaRecovery=-1000},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Combat.Simulate(.25f,1);Assert.Equal(0,f.Actor.Resources.Stamina);
        f.Combat.RestoreStamina(f.Id,50);f.Combat.Simulate(.25f,2);Assert.Equal(0,f.Actor.Resources.Stamina);
    }

    [Theory][InlineData(.05f)][InlineData(.5f)]
    public void EffectExpiryIntegratesItsOldRateBeforeScalingToTheNewMaximum(float delta)
    {
        var data=CommonResourceTests.Document();var effect=data["effects"]!.AsArray().Last()!;
        effect["durationSeconds"]=.25;effect["modifiers"]!["maxHealth"]=100;effect["modifiers"]!["healthRecovery"]=4;
        var f=new ChannelingTests.Fight(data);var baseline=f.Actor.Stats with {HealthRecovery=0,ManaRecovery=0};
        f.Combat.ApplyEquipment(f.Id,(baseline,f.Actor.Weapon,f.Actor.AttackInterval));f.Actor.Health=baseline.MaxHealth*.4;
        Assert.True(f.Combat.ApplyEffect(f.Id,f.Id,"resource_capacity"));
        for(uint tick=1;tick<=Math.Round(.5/delta);tick++)f.Combat.Simulate(delta,tick);
        var expected=(.4*(baseline.MaxHealth+100)+1)/(baseline.MaxHealth+100)*baseline.MaxHealth;
        Assert.Equal(expected,f.Actor.Health,7);Assert.Equal(baseline.MaxHealth,f.Actor.Stats.MaxHealth);
    }

    [Fact] public void CombatPassiveExpiryUsesItsOwnBoundaryInsideALargeStep()
    {
        var data=PassiveAbilityTests.Document();var passive=data["abilities"]!.AsArray().Last()!["passive"]!;
        passive["conditions"]=new System.Text.Json.Nodes.JsonObject{["inCombat"]=true};
        passive["modifiers"]=new System.Text.Json.Nodes.JsonObject{["healthRecovery"]=4};
        var f=new ChannelingTests.Fight(data);f.Actor.Health=10;
        f.Combat.ApplyEquipment(f.Id,(f.Actor.BaseDerivedStats with {HealthRecovery=0},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Combat.RestoreCombatPresence(f.Id,.25,0);f.Combat.Simulate(.5f,1);
        Assert.Equal(11,f.Actor.Health,8);Assert.False(f.Combat.InCombat(f.Id));
    }

    [Theory][InlineData(.05f)][InlineData(.25f)][InlineData(1f)]
    public void SignedFlowFromFullStopsAtZeroAndDeathHappensOnce(float delta)
    {
        var f=new ChannelingTests.Fight();f.Actor.Health=5;var deaths=0;double at=0;
        f.Combat.Defeated+=id=>{if(id==f.Id){deaths++;at=f.Combat.Time;}};
        f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {HealthRecovery=-10,ManaRecovery=-1000,StaminaRecovery=-1000},f.Actor.Weapon,f.Actor.AttackInterval));
        for(uint tick=1;tick<=Math.Ceiling(2/delta);tick++)f.Combat.Simulate(delta,tick);
        Assert.Equal(0,f.Actor.Health);Assert.Equal(0,f.Mana);Assert.Equal(0,f.Actor.Resources.Stamina);
        Assert.Equal(1,deaths);Assert.Equal(.5,at,8);Assert.True(f.Actor.DiedFromRegeneration);
        var death=Assert.Single(f.Combat.DeathLogs(f.Id));
        Assert.Contains(death.Entries,e=>e.Kind==CombatJournalKind.Death && e.Source=="Отрицательная регенерация");
    }

    [Theory][InlineData(.05f)][InlineData(.25f)]
    public void ChannelAndNegativeManaShareAnExactFundedBoundary(float delta)
    {
        var f=new ChannelingTests.Fight(delta:delta,mana:3);f.Cast();
        f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {ManaRecovery=-1},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Finish();Assert.Equal(2,f.Hits.Count);Assert.Equal(0,f.Mana,8);
        Assert.Equal(AbilityOutcome.NoMana,f.Abilities.Results[f.Id].Outcome);
    }

    [Theory][InlineData(.05f)][InlineData(.25f)]
    public void PositiveManaCanFundAChannelWhileAtCapacity(float delta)
    {
        var data=ChannelingTests.Document();data["abilities"]![1]!["manaCost"]=0;
        var f=new ChannelingTests.Fight(data,delta:delta);
        f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {ManaRecovery=3},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Cast();f.Finish();Assert.Equal(10,f.Hits.Count);Assert.Equal(f.Actor.Stats.MaxMana,f.Mana,8);
    }

    [Fact] public void SavedMaximumContextRejectsInvalidVersionsBoundsAndNonfiniteCapacities()
    {
        new SavedResourceMaxima(1,100,0,0).Validate(50,0,0);
        Assert.Throws<InvalidDataException>(()=>new SavedResourceMaxima(2,100,10,10).Validate(1,1,1));
        Assert.Throws<InvalidDataException>(()=>new SavedResourceMaxima(1,double.NaN,10,10).Validate(1,1,1));
        Assert.Throws<InvalidDataException>(()=>new SavedResourceMaxima(1,100,0,0).Validate(1,1,0));
    }

    [Fact] public void RegenerationDeathStopsChannelBeforeItsCoincidentPulse()
    {
        var f=new ChannelingTests.Fight();f.Cast();f.Actor.Health=1;
        f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {HealthRecovery=-10},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Step();f.Step();Assert.Equal(0,f.Actor.Health);Assert.Empty(f.Hits);
        Assert.Null(f.Actor.Action);Assert.Equal(0,f.Skill.PendingEndCharges);
        Assert.Single(f.Combat.DeathLogs(f.Id));
    }

    [Fact] public void AggregateRegenerationDeathDoesNotAssignTheRecentPvpAttackerAsKiller()
    {
        var w=PvpTests.World();w.PvpClock=()=>100000;
        var attacker=w.AddPlayer(42,new(1),PvpTests.Initial(w));
        var victim=w.AddPlayer(43,new(2),PvpTests.Initial(w,-4.5f));
        Assert.Equal(PvpOutcome.Accepted,PvpTests.Command(w,42,1,PvpMode.Criminal));
        Assert.True(w.Combat!.ApplyAbilityDamage(victim.EntityId,1,attacker.EntityId)>0);
        var actor=w.Combat.Get(victim.EntityId);actor.Health=1;
        w.Combat.ApplyEquipment(victim.EntityId,(actor.Stats with {HealthRecovery=-100},actor.Weapon,actor.AttackInterval));
        w.Simulate(.05f);
        Assert.True(w.PrivatePvp(victim.EntityId).Dead);Assert.True(w.PrivatePvp(victim.EntityId).PvpDeath);
        Assert.Equal(0,w.PrivatePvp(attacker.EntityId).Pk);Assert.Equal(-10,w.PrivatePvp(attacker.EntityId).Reputation);
        var saved=w.CaptureCharacter(43);Assert.Single(saved.DeathLogs!);
        w.Simulate(.05f);Assert.Equal(saved.Progression!.Experience,w.CaptureCharacter(43).Progression!.Experience);
    }
}
