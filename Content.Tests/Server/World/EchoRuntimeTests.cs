using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class EchoRuntimeTests
{
    private static ServerWorld Create(ContentCatalog? catalog=null)=>new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
        catalog:catalog??ContentCatalogTests.Load(),echoes:Options.Create(new EchoOptions{Enabled=true}),hitRoll:()=>0,criticalRoll:()=>1);
    private static void Step(ServerWorld world,int count=1)
    {for(var i=0;i<count;i++){world.Combat!.ClearResults();world.Abilities!.ClearResults();world.Echoes!.ClearResults();world.Simulate(.05f);}}
    [Fact]public void OwnershipDoesNotSpawnBodyAndExplicitSummonUsesTenSecondsAndOwnerMana()
    {
        var w=Create();var player=w.AddPlayer(1,new(1),EchoTests.WithEcho(w));var owner=w.Combat!.Get(player.EntityId);
        w.Combat.SetManaRecoveryConnected(owner.Id,false);var mana=owner.Resources.Mana;
        var row=Assert.Single(w.Echoes!.Roster(owner.Id,0).Entries);Assert.Equal(EchoAvailability.Ready,row.Availability);
        Assert.True(w.TryQueueEchoSummon(1,new(1,row.Handle)));Step(w,200);
        Assert.Empty(w.Echoes.Loadout(owner.Id,w.Tick).Slots);Assert.Equal(mana,owner.Resources.Mana);
        Assert.Equal(EchoAvailability.Summoning,Assert.Single(w.Echoes.Roster(owner.Id,w.Tick).Entries).Availability);
        Step(w);var slot=Assert.Single(w.Echoes.Loadout(owner.Id,w.Tick).Slots);var body=w.Combat.Get(slot.EntityId);
        Assert.Equal(mana-20,owner.Resources.Mana);Assert.Equal(CombatEntityKind.Echo,body.Kind);Assert.Equal(owner.Id,body.CompanionOwner);
        Assert.Equal(110,body.Health,10);Assert.Equal(140,body.Resources.Mana);Assert.Equal(100,body.Resources.Stamina);
        Assert.Equal(EchoSummonOutcome.Completed,w.Echoes.SummonResults[owner.Id].Outcome);
        Assert.False(w.TryQueueEchoSummon(1,new(1,row.Handle)));Step(w);
        Assert.Empty(w.Echoes.SummonResults);Assert.Single(w.Echoes.Loadout(owner.Id,w.Tick).Slots);
    }
    [Fact]public void MagicalSummonIsInterruptedBySilenceWithHalfPriceAndNoBody()
    {
        var w=Create();var p=w.AddPlayer(1,new(1),EchoTests.WithEcho(w));w.Combat!.SetManaRecoveryConnected(p.EntityId,false);
        var row=Assert.Single(w.Echoes!.Roster(p.EntityId,0).Entries);var mana=w.Abilities!.Mana(p.EntityId);
        Assert.True(w.TryQueueEchoSummon(1,new(1,row.Handle)));Step(w);
        Assert.True(w.Combat.ApplyStatusControl(default,p.EntityId,40,11,CrowdControlKind.Silence,1));
        w.Echoes.Simulate(0,w.Tick);
        Assert.Equal(mana-10,w.Abilities.Mana(p.EntityId));Assert.Empty(w.Echoes.Loadout(p.EntityId,w.Tick).Slots);
        Assert.Equal(EchoSummonOutcome.Interrupted,w.Echoes.SummonResults[p.EntityId].Outcome);
        Step(w);
        Assert.Equal(SavedEchoAvailability.Ready,Assert.Single(w.CaptureCharacter(1).OwnedEchoes!).Availability);
    }
    [Fact]public void VoluntarySummonCancelMatchesExecutionAndIsFreeWithoutCreatingBody()
    {
        var w=Create();var p=w.AddPlayer(1,new(1),EchoTests.WithEcho(w));w.Combat!.SetManaRecoveryConnected(p.EntityId,false);
        var entry=Assert.Single(w.Echoes!.Roster(p.EntityId,0).Entries);var mana=w.Abilities!.Mana(p.EntityId);
        Assert.True(w.TryQueueEchoSummon(1,new(1,entry.Handle)));Step(w,10);
        var pending=Assert.Single(w.Echoes.Roster(p.EntityId,w.Tick).Entries);
        Assert.Equal(EchoAvailability.Summoning,pending.Availability);Assert.Equal(1u,pending.ActionSequence);Assert.True(pending.Execution>0);
        Assert.False(w.TryQueueEchoSummonControl(1,new(1,pending.ActionSequence,pending.Execution+1)));Step(w);
        w.Combat.ClearResults();w.Echoes.ClearResults();
        Assert.True(w.TryQueueEchoSummonControl(1,new(2,pending.ActionSequence,pending.Execution)));w.Simulate(.05f);
        Assert.Equal(EchoSummonOutcome.Cancelled,w.Echoes.SummonResults[p.EntityId].Outcome);Assert.Equal(mana,w.Abilities.Mana(p.EntityId));
        Assert.Equal(EchoAvailability.Ready,Assert.Single(w.Echoes.Roster(p.EntityId,w.Tick).Entries).Availability);
        Assert.Empty(w.Echoes.Loadout(p.EntityId,w.Tick).Slots);Assert.False(w.TryQueueEchoSummonControl(1,new(2,pending.ActionSequence,pending.Execution)));
    }
    [Fact]public void EchoDeathNeedsRestorationAndNeverCreatesPlayerDeathOrAllowsAnotherSummon()
    {
        var w=Create();var p=w.AddPlayer(1,new(1),EchoTests.WithEcho(w));var id=EchoTests.Summon(w,1);
        Assert.True(w.Combat!.PayHealth(id,w.Combat.Get(id).Health,true));Step(w);
        var dead=Assert.Single(w.CaptureCharacter(1).OwnedEchoes!);Assert.Equal(SavedEchoAvailability.RequiresRestoration,dead.Availability);
        Assert.True(dead.DeathId>0);Assert.False(w.Combat.CaptureLife(p.EntityId).Dead);Assert.Empty(w.Echoes!.Loadout(p.EntityId,w.Tick).Slots);
        var row=Assert.Single(w.Echoes.Roster(p.EntityId,w.Tick).Entries);
        Assert.True(w.TryQueueEchoSummon(1,new(2,row.Handle)));Step(w);
        Assert.Equal(EchoSummonOutcome.Unavailable,w.Echoes.SummonResults[p.EntityId].Outcome);Assert.False(w.Combat.TryGet(id,out _));
    }
    [Fact]public void RecallIsBlockedInCombatThenPreservesResourcesAcrAndCooldownWithoutRefillOnSummon()
    {
        var w=Create();long now=100000;w.PvpClock=()=>now;var p=w.AddPlayer(1,new(1),EchoTests.WithEcho(w));var id=EchoTests.Summon(w,1);
        var actor=w.Combat!.Get(id);actor.Health=41;actor.Resources.Mana=27;actor.Resources.Stamina=33;
        var initial=Assert.Single(w.CaptureCharacter(1).OwnedEchoes!);
        w.Abilities!.Restore(id,initial.ActorState() with {Cooldowns=[new("mira_signature",7)]},0);
        Assert.True(w.Combat.ApplyStatusControl(default,id,40,15,CrowdControlKind.Root,.4));Step(w,10);
        var row=Assert.Single(w.Echoes!.Roster(p.EntityId,w.Tick).Entries);
        Assert.True(w.TryQueueEchoRecall(1,new(1,row.Handle)));Assert.Equal(EchoRecallOutcome.InCombat,w.Echoes.RecallResults[p.EntityId].Outcome);
        Step(w);now+=31000;var before=Assert.Single(w.CaptureCharacter(1).OwnedEchoes!);
        Assert.True(w.TryQueueEchoRecall(1,new(2,row.Handle)));Step(w);
        var ready=Assert.Single(w.CaptureCharacter(1).OwnedEchoes!);Assert.Equal(SavedEchoAvailability.Ready,ready.Availability);
        Assert.Equal(before.Health,ready.Health);Assert.Equal(before.Mana,ready.Mana);Assert.Equal(before.Defense,ready.Defense);
        Assert.Equal(before.CombatControl.Acr,ready.CombatControl.Acr);
        Assert.Equal(before.CombatControl with {Acr=ready.CombatControl.Acr},ready.CombatControl);Assert.Equal(before.Cooldowns,ready.Cooldowns);
        Assert.False(w.Combat.TryGet(id,out _));Assert.False(w.TryQueueEchoRecall(1,new(2,row.Handle)));
        var next=EchoTests.Summon(w,1,ready.InstanceId,2);Assert.NotEqual(id,next);
        Assert.Equal(ready.Health,w.Combat.Get(next).Health,5);Assert.Equal(ready.Mana,w.Abilities!.Mana(next),5);Assert.Equal(ready.Defense.Stamina,w.Combat.Get(next).Resources.Stamina,5);
    }
    [Fact]public void LeavingSevenMeterRadiusForgetsOpponentAndReturningNeedsNewContact()
    {
        var w=Create();var p=w.AddPlayer(1,new(1),EchoTests.WithEcho(w) with {X=-7,Z=2});var body=EchoTests.Summon(w,1);
        var durableTarget=w.Combat!.Get(w.TrainingTargetId);
        w.Combat.ApplyEquipment(durableTarget.Id,(durableTarget.BaseDerivedStats with {MaxHealth=10000,HealthRecovery=0},durableTarget.Weapon,durableTarget.AttackInterval));
        durableTarget.Health=10000;
        w.Echoes!.Alert(p.EntityId,w.TrainingTargetId);Step(w,5);Assert.True(w.Echoes.TryGet(body,out var echo));Assert.Equal(w.TrainingTargetId,echo.Target);
        Assert.True(w.TryApplyMove(1,new(1,w.Tick,new(6,2),InputOrder:2)));Step(w,110);Assert.False(echo.Target.IsValid);
        Assert.True(w.TryApplyMove(1,new(2,w.Tick,new(-7,2),InputOrder:3)));var hit=false;
        for(var i=0;i<110;i++){Step(w);hit|=w.Combat!.Events.Any(e=>e.AttackerId==body);}
        Assert.False(hit);Assert.False(echo.Target.IsValid);
        w.Echoes.Alert(p.EntityId,w.TrainingTargetId);Step(w,6);Assert.Equal(w.TrainingTargetId,echo.Target);
    }
    [Fact]public void CrashMovesActiveEchoToReadyWithoutRefillingItsResourcesOrGrantingAnActiveBody()
    {
        var first=Create();var owner=first.AddPlayer(1,new(1),EchoTests.WithEcho(first));var body=EchoTests.Summon(first,1);
        Assert.True(first.Combat!.PayHealth(body,10));
        var resource=first.Combat.Get(body).Resources;resource.Mana=7;resource.Stamina=13;
        var saved=first.CaptureCharacter(1);var original=Assert.Single(saved.OwnedEchoes!);
        Assert.Equal(SavedEchoAvailability.Active,original.Availability);
        var restarted=Create();var restored=restarted.AddPlayer(1,new(2),saved);
        Assert.Empty(restarted.Echoes!.Loadout(restored.EntityId,restarted.Tick).Slots);
        var ready=Assert.Single(restarted.CaptureCharacter(1).OwnedEchoes!);
        Assert.Equal(SavedEchoAvailability.Ready,ready.Availability);
        Assert.Equal(original.Health,ready.Health);Assert.Equal(7,ready.Mana);Assert.Equal(13,ready.Defense.Stamina);
        Assert.Equal(original.InstanceId,ready.InstanceId);Assert.Empty(restarted.Echoes.Loadout(restored.EntityId,restarted.Tick).Slots);
    }
    [Fact]public void OwnedRosterExceedsThreeButFourthActiveSummonIsRejectedWithoutPayment()
    {
        var w=Create();w.AddPlayer(1,new(1),EchoTests.WithEcho(w));var seed=w.CaptureCharacter(1);
        var ready=Assert.Single(seed.OwnedEchoes!);w.RemovePlayer(1);
        var owned=Enumerable.Range(0,9).Select(_=>ready with {InstanceId=Guid.NewGuid()}).ToArray();
        var p=w.AddPlayer(2,new(2),seed with {OwnedEchoes=owned});var page=w.Echoes!.Roster(p.EntityId,w.Tick);
        Assert.Equal(9u,page.Total);Assert.Equal(8,page.Entries.Count);
        for(uint i=0;i<3;i++)EchoTests.Summon(w,2,owned[i].InstanceId,i+1);
        var mana=w.Abilities!.Mana(p.EntityId);w.Combat!.SetManaRecoveryConnected(p.EntityId,false);
        Assert.True(w.TryQueueEchoSummon(2,new(4,page.Entries[3].Handle)));Step(w);
        Assert.Equal(EchoSummonOutcome.ActiveLimit,w.Echoes.SummonResults[p.EntityId].Outcome);Assert.Equal(mana,w.Abilities.Mana(p.EntityId));
        Assert.True(w.TryQueueEchoRoster(2,new(1,1)));Assert.Single(w.Echoes.Roster(p.EntityId,w.Tick).Entries);
        Assert.False(w.TryQueueEchoRoster(2,new(2,uint.MaxValue)));
    }
    [Fact]public void RootStopsEchoMovementButAllowsItsInRangeBasicAttack()
    {
        var w=Create();var p=w.AddPlayer(1,new(1),EchoTests.WithEcho(w) with {X=-7,Z=2});var id=EchoTests.Summon(w,1);
        var position=w.Combat!.Get(id).Position;
        Assert.True(w.Combat.ApplyStatusControl(default,id,40,20,CrowdControlKind.Root,2));
        w.Echoes!.Alert(p.EntityId,w.TrainingTargetId);var hits=0;
        for(var i=0;i<20;i++){Step(w);hits+=w.Combat.Events.Count(e=>e.AttackerId==id&&e.Damage>0);Assert.Equal(position,w.Combat.Get(id).Position);}
        Assert.True(hits>0);Assert.True(w.Combat.Get(id).Controls.Has(CrowdControlKind.Root));
    }
    [Fact]public void EchoAttackCadenceUsesCommonTimelineBeyondTickAndPerceptionRates()
    {
        var w=Create();var p=w.AddPlayer(1,new(1),EchoTests.WithEcho(w) with {X=-7,Z=2});var id=EchoTests.Summon(w,1);
        var actor=w.Combat!.Get(id);var target=w.Combat.Get(w.TrainingTargetId);
        w.Combat.ApplyEquipment(target.Id,(target.BaseDerivedStats with {MaxHealth=100000,HealthRecovery=0},target.Weapon,target.AttackInterval));target.Health=100000;
        w.Combat.ApplyEquipment(id,(actor.BaseDerivedStats with {AttackSpeedMultiplier=actor.Weapon.AttackIntervalSeconds/.03},actor.Weapon,.03));
        w.Echoes!.Alert(p.EntityId,target.Id);var executions=new HashSet<ulong>();
        for(var i=0;i<10;i++){Step(w);foreach(var hit in w.Combat.Events.Where(e=>e.AttackerId==id))Assert.True(executions.Add(hit.ExecutionId));}
        Assert.InRange(executions.Count,14,17);
        w.Echoes.TryGet(id,out var body);Assert.Equal(target.Id,body.Target);
    }
    [Fact]public void RecallPreservesEffectsAcrossRegionButLoginPolicySurvivesTransferBeforeSummon()
    {
        var document=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        document["effects"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"recall_buff","durationSeconds":120,"disposition":"Beneficial","persistThroughLogout":false}
            """));
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(document.ToJsonString()));
        ServerWorld World(){var world=Create(catalog);world.PvpClock=()=>100000;return world;}
        var source=World();var owner=source.AddPlayer(1,new(1),EchoTests.WithEcho(source));var first=EchoTests.Summon(source,1);
        source.Combat!.ApplyEffect(default,first,"recall_buff");Assert.Single(source.Combat.Get(first).Effects);
        var handle=Assert.Single(source.Echoes!.Roster(owner.EntityId,source.Tick).Entries).Handle;
        Assert.True(source.TryQueueEchoRecall(1,new(1,handle)));source.Simulate(.05f);
        var recalled=source.CaptureCharacter(1);Assert.False(Assert.Single(recalled.OwnedEchoes!).PendingLogoutRestore);
        source.RemovePlayer(1);
        var region=World();region.AddPlayer(1,new(1),recalled,restoringFromLogout:false);var second=EchoTests.Summon(region,1);
        Assert.Single(region.Combat!.Get(second).Effects); // Regional transfer is not a logout.
        region.FinishDeparture(1);var exited=region.CaptureCharacter(1);region.RemovePlayer(1);
        var login=World();login.AddPlayer(1,new(1),exited);var pending=login.CaptureCharacter(1);
        Assert.True(Assert.Single(pending.OwnedEchoes!).PendingLogoutRestore);login.RemovePlayer(1);
        var next=World();next.AddPlayer(1,new(1),pending,restoringFromLogout:false);var third=EchoTests.Summon(next,1);
        Assert.Empty(next.Combat!.Get(third).Effects);Assert.False(Assert.Single(next.CaptureCharacter(1).OwnedEchoes!).PendingLogoutRestore);
    }
}
