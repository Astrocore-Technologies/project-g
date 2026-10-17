using System.Numerics;
using System.Text.Json.Nodes;
using Content.Server.Persistence;
using Content.Shared.Combat;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ToggleAbilityTests
{
    [Fact]public void ABackgroundWeaponPulseSettlesItsBreakWhileAnotherCastOwnsTheOrdinaryLane()
    {
        var data=Document();data["abilities"]![1]!["action"]!["capabilities"]="Weapon, Magical";
        data["abilities"]![0]!["action"]!["preparation"]!["seconds"]=.5;
        var f=new ChannelingTests.Fight(data,weapon:true);f.Cast();
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,1,Vector2.UnitX),0));f.Step();
        var action=f.Actor.Action;Assert.NotNull(action);Assert.Equal("test_projectile",action.DefinitionId);
        for(var i=0;i<5 && f.Hits.Count==0;i++)f.Step();
        Assert.Single(f.Hits);Assert.Equal("unarmed",f.Actor.Weapon.Id);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);
        Assert.Same(action,f.Actor.Action);Assert.False(action.Finished);Assert.Equal(0,f.Skill.PendingEndCharges);
    }

    [Fact]public void BackgroundWeaponToggleWithAnActivationPulseBreaksAndPublishesOffInTheSameTick()
    {
        var data=Document();data["abilities"]![1]!["action"]!["capabilities"]="Weapon, Magical";
        data["abilities"]![1]!["channel"]!["pulseAtStart"]=true;
        var f=new ChannelingTests.Fight(data,weapon:true,delta:.25f);f.Cast();
        Assert.Single(f.Hits);Assert.Equal("unarmed",f.Actor.Weapon.Id);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);
        Assert.Empty(f.Abilities.ActiveStates);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Equal(5,f.Skill.ReadyInSeconds);
        Assert.Equal(138,f.Mana);f.Step();Assert.Single(f.Hits);Assert.Equal(138,f.Mana);
    }

    [Fact]public void BackgroundWeaponToggleBreaksAfterItsCompletedPulseAndStopsBeforeAnyMorePaidWork()
    {
        var data=Document(stops:"Skill");data["abilities"]![1]!["action"]!["capabilities"]="Weapon, Magical";
        data["creatures"]![0]!["abilityIds"]!.AsArray().Add("sword_thrust");
        var f=new ChannelingTests.Fight(data,weapon:true,delta:.25f);f.Cast();
        for(var i=0;i<10 && f.Hits.Count==0;i++)f.Step();Assert.NotEmpty(f.Hits);
        Assert.Single(f.Hits);Assert.Equal("unarmed",f.Actor.Weapon.Id);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);
        Assert.Equal(137.7,f.Mana,8);Assert.Empty(f.Abilities.ActiveStates);Assert.Equal(0,f.Skill.PendingEndCharges);
        Assert.Equal(4.85,f.Skill.ReadyInSeconds,7);var mana=f.Mana;
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,20,Vector2.UnitY),0));f.Step();
        Assert.Equal(AbilityOutcome.NeedsSword,f.Abilities.Results[f.Id].Outcome);Assert.Null(f.Actor.Action);
        Assert.Equal(0,f.Abilities.Loadout(f.Id,f.World.Tick).Abilities.Single(s=>s.Id==20).ReadyInSeconds);
        Assert.Equal(mana,f.Mana);Assert.Single(f.Hits);
        Assert.Equal(AbilityAvailability.NeedsWeapon,f.Skill.Availability);
    }

    [Fact]public void BackgroundWeaponToggleCannotActivateWithoutAnEquippedWeapon()
    {
        var data=Document();data["abilities"]![1]!["action"]!["capabilities"]="Weapon, Magical";
        var f=new ChannelingTests.Fight(data);f.Combat.ApplyEquipment(f.Id,f.Combat.PrepareEquipment(f.Player.BaseStats,[]));
        Assert.True(f.World.TryQueueAbility(42,new(1,f.World.Tick,2,f.Actor.Position),0));f.Step();
        Assert.Equal(AbilityOutcome.NeedsWeapon,f.Abilities.Results[f.Id].Outcome);Assert.Equal(140,f.Mana);
        Assert.Equal(ToggleState.Off,f.Skill.ToggleState);Assert.Equal(1,f.Skill.Charges);
    }

    internal static JsonNode Document(bool occupies=false,string stops="None",bool preparing=false)
    {
        var data=ChannelingTests.Document();var skill=data["abilities"]![1]!;
        skill["action"]!["form"]="Toggle";skill["action"]!["preparation"]!["cancel"]="Voluntary, Skill, Block, Parry, Dodge";
        skill["action"]!["active"]!["cancel"]="Voluntary";
        if(preparing)skill["action"]!["preparation"]!["seconds"]=.5;
        skill["toggle"]=new JsonObject{["occupiesAction"]=occupies,["stopOn"]=stops};skill["channel"]!["maximumSeconds"]=null;return data;
    }
    private static void Off(ChannelingTests.Fight f,uint sequence=2)
    {Assert.True(f.World.TryQueueAbility(42,new(sequence,f.World.Tick,2,new(999,999),Intent:AbilityIntent.Deactivate),0));f.Step();}

    [Fact]public void BackgroundToggleWorksWhileTheOrdinaryLaneIsFreeAndRepeatedActivationIsIdempotent()
    {
        var f=new ChannelingTests.Fight(Document());f.Cast();Assert.Equal(ToggleState.Active,f.Skill.ToggleState);Assert.Null(f.Actor.Action);
        Assert.Equal(AbilityPhase.Sustaining,Assert.Single(f.Abilities.ActiveStates).Phase);Assert.Equal(1,f.Skill.PendingEndCharges);
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,2,f.Actor.Position),0));f.Step();
        Assert.Equal(AbilityOutcome.AlreadyActive,f.Abilities.Results[f.Id].Outcome);Assert.Equal(137.85,f.Mana,7);Assert.Single(f.Abilities.ActiveStates);
        Off(f,3);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Equal(5,f.Skill.ReadyInSeconds);
        var mana=f.Mana;Off(f,4);Assert.Equal(mana,f.Mana);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void AQuickToggleBurstKeepsOnlyItsLastDesiredStateWithinOneTick(bool enableLast)
    {
        var f=new ChannelingTests.Fight(Document());var aim=f.Combat.Get(f.World.TrainingTargetId).Position;
        Assert.True(f.World.TryQueueAbility(42,new(1,0,2,aim),0));
        Assert.True(f.World.TryQueueAbility(42,new(2,0,2,aim,Intent:AbilityIntent.Deactivate),0));
        if(enableLast)Assert.True(f.World.TryQueueAbility(42,new(3,0,2,aim),0));f.Step();
        Assert.Equal(enableLast?ToggleState.Active:ToggleState.Off,f.Skill.ToggleState);Assert.Equal(enableLast?138:140,f.Mana);
        Assert.Equal(enableLast?1:0,f.Skill.PendingEndCharges);
        Assert.Equal(enableLast?AbilityOutcome.Accepted:AbilityOutcome.Cancelled,f.Abilities.Results[f.Id].Outcome);
    }

    [Fact]public void TurningOffABackgroundToggleDoesNotCancelTheOrdinaryCastOrItsRecovery()
    {
        var data=Document();data["abilities"]![0]!["action"]!["preparation"]!["seconds"]=.5;
        var f=new ChannelingTests.Fight(data);f.Cast();
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,1,Vector2.UnitX),0));f.Step();var action=f.Actor.Action;
        Assert.NotNull(action);Assert.Equal("test_projectile",action.DefinitionId);Assert.Equal(ToggleState.Active,f.Skill.ToggleState);
        Off(f,3);Assert.Same(action,f.Actor.Action);Assert.False(action.Finished);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);
    }

    [Fact]public void AToggleCanExplicitlyOwnTheSingleOrdinaryLane()
    {
        var f=new ChannelingTests.Fight(Document(true));f.Cast();Assert.True(f.Actor.IsCasting);Assert.Equal(AbilityPhase.Channeling,Assert.Single(f.Abilities.ActiveStates).Phase);
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,1,Vector2.UnitX),0));f.Step();Assert.Equal(AbilityOutcome.Buffered,f.Abilities.Results[f.Id].Outcome);
        Off(f,3);Assert.Null(f.Actor.Action);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);
    }

    [Theory][InlineData("None",true)][InlineData("EnemyInterrupt",false)]
    public void BackgroundInterruptionUsesItsExplicitStopPolicy(string stops,bool remains)
    {
        var f=new ChannelingTests.Fight(Document(stops:stops));f.Cast();f.Abilities.InterruptCast(f.Id);f.Step();
        Assert.Equal(remains?ToggleState.Active:ToggleState.Off,f.Skill.ToggleState);Assert.Equal(remains?1:0,f.Skill.PendingEndCharges);
    }

    [Theory][InlineData("BasicAttack",true)][InlineData("Defense",true)][InlineData("Skill",true)][InlineData("None",false)]
    public void AcceptedActionsStopOnlyTheTogglesWhoseDataRequestsThatEvent(string trigger,bool stops)
    {
        var f=new ChannelingTests.Fight(Document(stops:trigger));f.Cast();
        if(trigger=="Defense")Assert.True(f.World.TryQueueDefense(42,new(1,DefenseAction.Parry,Vector2.UnitY)));
        else if(trigger=="Skill")Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,1,Vector2.UnitX),0));
        else Assert.True(f.World.TryQueueAttack(42,new(1,0,Vector2.UnitX)));
        f.Step();Assert.Equal(stops?ToggleState.Off:ToggleState.Active,f.Skill.ToggleState);
    }

    [Fact]public void ARejectedBasicAttackDoesNotTriggerAnAuthoredStop()
    {
        var f=new ChannelingTests.Fight(Document(stops:"BasicAttack"));f.Cast();
        f.Actor.ReadyAt=100;Assert.True(f.World.TryQueueAttack(42,new(1,0,Vector2.UnitX)));f.Step();
        Assert.Equal(AttackOutcome.Buffered,f.Combat.Results[f.Id].Outcome);Assert.Equal(ToggleState.Active,f.Skill.ToggleState);
        for(var i=0;i<4;i++)f.Step();Assert.Equal(0,f.Combat.Inputs.WaitingCount);Assert.Equal(ToggleState.Active,f.Skill.ToggleState);
    }

    [Fact]public void AcceptedMovementUsesTheAuthoredToggleStopWithoutChangingOtherActionOwnership()
    {
        var f=new ChannelingTests.Fight(Document(stops:"Movement"));f.Cast();
        Assert.True(f.World.TryApplyMove(42,new(1,0,f.Actor.Position+Vector2.UnitX)));
        Assert.Equal(ToggleState.Off,f.Skill.ToggleState);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Null(f.Actor.Action);
    }

    [Theory][InlineData(true)][InlineData(false)]
    public void SilenceInterruptsOnlyMagicalExecutionsEvenWhenBackgroundAndOrdinaryActionsHaveDifferentCapabilities(bool magicToggle)
    {
        var data=Document(stops:"EnemyInterrupt");data["abilities"]![1]!["action"]!["capabilities"]=magicToggle?"Magical":"Weapon";
        data["abilities"]![0]!["action"]!["capabilities"]=magicToggle?"Weapon":"Magical";
        data["abilities"]![0]!["action"]!["preparation"]!["seconds"]=.5;
        var f=new ChannelingTests.Fight(data);f.Cast();
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,1,Vector2.UnitX),0));f.Step();var ordinary=f.Actor.Action!;
        Assert.True(f.Combat.ApplyStatusControl(f.World.TrainingTargetId,f.Id,1,100,CrowdControlKind.Silence,1));
        Assert.Equal(magicToggle?ToggleState.Off:ToggleState.Active,f.Skill.ToggleState);
        Assert.Equal(!magicToggle,ordinary.Finished);
        if(magicToggle)Assert.Same(ordinary,f.Actor.Action);
    }

    [Fact]public void SilenceCanReachABackgroundMagicalToggleWithoutAnOrdinaryCast()
    {
        var f=new ChannelingTests.Fight(Document(stops:"EnemyInterrupt"));f.Cast();Assert.Null(f.Actor.Action);
        Assert.True(f.Combat.ApplyStatusControl(f.World.TrainingTargetId,f.Id,1,100,CrowdControlKind.Silence,1));Assert.Equal(ToggleState.Off,f.Skill.ToggleState);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void PreparationCanBeCancelledOrInterruptedWithoutAHalfPrice(bool enemy)
    {
        var f=new ChannelingTests.Fight(Document(preparing:true));f.Cast();Assert.Equal(ToggleState.Preparing,f.Skill.ToggleState);Assert.Equal(140,f.Mana);
        if(enemy){f.Abilities.InterruptCast(f.Id);f.Step();}else Off(f);
        Assert.Equal(140,f.Mana);Assert.Equal(enemy?0:1,f.Skill.Charges);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);
        Assert.Equal(enemy?4.95:0,f.Skill.ReadyInSeconds,6);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void DeathAndFinalDepartureAlwaysEndToggleRegardlessOfOptionalStopEvents(bool death)
    {
        var f=new ChannelingTests.Fight(Document());f.Cast();f.Step();var mana=f.Mana;
        if(death)f.Combat.ApplyAbilityDamage(f.Id,100000,f.World.TrainingTargetId);else f.Abilities.FinishDeparture(f.Id);
        f.Step();Assert.Equal(mana,f.Mana);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void ExhaustionTurnsOffAndALateDeactivateCannotReactivateTheSkill()
    {
        var f=new ChannelingTests.Fight(Document(),mana:2.4,delta:.1f);f.Cast();
        for(var i=0;i<10&&f.Skill.ToggleState!=ToggleState.Off;i++)f.Step();
        Assert.Equal(0,f.Mana);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);Assert.Single(f.Hits);
        f.Abilities.ResetCooldown(f.Id,2);f.Combat.RestoreMana!(f.Id,10);var mana=f.Mana;Off(f);
        Assert.Equal(mana,f.Mana);Assert.Equal(1,f.Skill.Charges);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);
    }

    [Fact]public void CrashEndsToggleAndItsDeferredCooldownStartsAtLoginWithoutOfflineCredit()
    {
        var f=new ChannelingTests.Fight(Document());f.Cast();var saved=f.World.CaptureCharacter(42);
        var next=new ChannelingTests.Fight(Document(),saved:saved);next.Abilities.Restore(next.Id,saved,1000);
        Assert.Equal(ToggleState.Off,next.Skill.ToggleState);Assert.Equal(5,next.Skill.ReadyInSeconds);Assert.Equal(0,next.Skill.PendingEndCharges);Assert.Null(next.Actor.Action);
    }

    [Fact]public void CooldownReservationSurvivesReorderingTheLearnedSlots()
    {
        var f=new ChannelingTests.Fight(Document());f.Cast();var saved=f.World.CaptureCharacter(42).Progression!;
        f.Abilities.ApplyProgression(f.Id,saved with {Skills=saved.Skills.OrderBy(s=>s.DefinitionId=="test_ground_area"?1:0).ToArray()});
        Assert.Equal(ToggleState.Active,f.Skill.ToggleState);Off(f);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Equal(5,f.Skill.ReadyInSeconds);
        Assert.Equal(0,f.Abilities.Loadout(f.Id,f.World.Tick).Abilities.Single(p=>p.Id==1).ReadyInSeconds);
    }

    [Fact]public void RemovingTheSkillFromTheBarEndsItsToggleBeforeCopyingItsCooldown()
    {
        var f=new ChannelingTests.Fight(Document());f.Cast();var saved=f.World.CaptureCharacter(42).Progression!;
        f.Abilities.ApplyProgression(f.Id,saved with {Skills=saved.Skills.Select(s=>s.DefinitionId=="test_ground_area"?s with {Slot=0}:s).ToArray()});
        var cooldown=f.Abilities.CaptureCooldowns(f.Id).Single(c=>c.AbilityId=="test_ground_area");Assert.Equal(0,cooldown.PendingEnd);Assert.Equal(5,cooldown.Seconds);
        Assert.DoesNotContain(f.Abilities.Loadout(f.Id,f.World.Tick).Abilities,p=>p.Id==2);
    }

    [Fact]public void SourceDeathInsideAnActivationCallbackDoesNotLeaveAnOrphanToggleOrReservedCharge()
    {
        var data=Document();data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"activation_callback","durationSeconds":1,"modifiers":{"magicAttack":1}}"""));
        data["abilities"]![1]!["effects"]=new JsonArray(JsonNode.Parse("""{"trigger":"Activated","target":"Self","effectId":"activation_callback"}"""));
        var f=new ChannelingTests.Fight(data);var reacted=false;
        f.Combat.EffectStatsChanged+=id=>{if(id==f.Id&&!reacted){reacted=true;f.Combat.ApplyAbilityDamage(id,100000,f.World.TrainingTargetId);}};
        Assert.True(f.World.TryQueueAbility(42,new(1,0,2,f.Combat.Get(f.World.TrainingTargetId).Position),0));f.Step();
        Assert.Equal(AbilityOutcome.InvalidState,f.Abilities.Results[f.Id].Outcome);Assert.Equal(0,f.Actor.Health);Assert.Equal(ToggleState.Off,f.Skill.ToggleState);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Equal(5,f.Skill.ReadyInSeconds);
    }

    [Theory][InlineData("missing")][InlineData("unexpected")][InlineData("flags")][InlineData("missingOwnership")][InlineData("backgroundRecovery")][InlineData("interruptionConflict")]
    public void InvalidTogglePoliciesFailBeforeWorldStartup(string invalid)
    {
        var data=Document();var skill=data["abilities"]![1]!;
        switch(invalid)
        {
            case "missing":skill.AsObject().Remove("toggle");break;case "unexpected":skill["action"]!["form"]="Channel";break;
            case "flags":skill["toggle"]!["stopOn"]="Unknown";break;case "missingOwnership":skill["toggle"]!.AsObject().Remove("occupiesAction");break;
            case "backgroundRecovery":skill["action"]!["recovery"]=new JsonObject{["seconds"]=1};break;
            case "interruptionConflict":skill["toggle"]!["stopOn"]="EnemyInterrupt";skill["action"]!["interruptible"]=false;break;
        }
        Assert.Throws<InvalidDataException>(()=>ChannelingTests.Parse(data));
    }
}
