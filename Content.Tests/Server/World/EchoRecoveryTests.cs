using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Server.Combat;
using Content.Shared.Combat;
using System.Text;
using System.Text.Json.Nodes;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Content.Tests.Server.Persistence;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class EchoRecoveryTests
{
    internal static ServerWorld Create(ContentCatalog? catalog=null)=>new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
        catalog:catalog??ContentCatalogTests.Load(),echoes:Options.Create(new EchoOptions{Enabled=true}),
        inventory:Options.Create(new InventoryOptions{Enabled=true}),hitRoll:()=>0,criticalRoll:()=>1);
    internal static CharacterState Dead(ServerWorld w,int seals=2)
    {
        w.PvpClock=()=>100000;w.AddPlayer(1,new(1),EchoTests.WithEcho(w));var state=w.CaptureCharacter(1);w.RemovePlayer(1);
        var echo=Assert.Single(state.OwnedEchoes!) with {Availability=SavedEchoAvailability.RequiresRestoration,Health=0,Mana=7,
            Defense=new(1,8,0,0),DeathId=5,SavedAtUnixMilliseconds=100000,Cooldowns=[new("mira_signature",7)]};
        return state with {OwnedEchoes=[echo],Inventory=new(){Items=Enumerable.Range(0,seals).Select(_=>
            new SavedItem(Guid.NewGuid(),"echo_restoration_seal",EquipmentSlot.None)).ToArray()}};
    }
    internal static void Step(ServerWorld w)
    {w.Combat!.ClearResults();w.Abilities!.ClearResults();w.Echoes!.ClearResults();w.Inventory!.ClearResults();w.Simulate(.05f);}

    [Fact]public void RecoveryConsumesExactlyOneSealFillsCurrentResourcesAndStillRequiresSummon()
    {
        var w=Create();var initial=Dead(w);var p=w.AddPlayer(1,new(1),initial);var owner=p.EntityId;
        var row=Assert.Single(w.Echoes!.Roster(owner,0).Entries);Assert.Equal(5ul,row.DeathId);
        var items=w.CaptureCharacter(1).Inventory!.Items;
        Assert.True(w.TryQueueEchoRestore(1,new(1,row.Handle,row.DeathId)));Step(w);
        Assert.Equal(EchoRestoreOutcome.Completed,w.Echoes.RestoreResults[owner].Outcome);
        var saved=w.CaptureCharacter(1);var ready=Assert.Single(saved.OwnedEchoes!);
        Assert.Equal(SavedEchoAvailability.Ready,ready.Availability);Assert.Equal(5ul,ready.DeathId);
        Assert.Equal(250,ready.Health,8);Assert.Equal(140,ready.Mana);Assert.Equal(100,ready.Defense.Stamina);
        Assert.InRange(Assert.Single(ready.Cooldowns).Seconds,6.94,6.96);Assert.Empty(w.Echoes.Loadout(owner,w.Tick).Slots);
        Assert.Equal(items[1],Assert.Single(saved.Inventory!.Items));Assert.False(w.TryQueueEchoRestore(1,new(1,row.Handle,row.DeathId)));
        Assert.True(w.TryQueueEchoRestore(1,new(2,row.Handle,row.DeathId)));Step(w);
        Assert.Equal(EchoRestoreOutcome.InvalidState,w.Echoes.RestoreResults[owner].Outcome);Assert.Single(w.CaptureCharacter(1).Inventory!.Items);
        var body=EchoTests.Summon(w,1,ready.InstanceId);Assert.Equal(ready.Health,w.Combat!.Get(body).Health,8);
    }

    [Theory][InlineData("seal",EchoRestoreOutcome.MissingSeal)][InlineData("owner",EchoRestoreOutcome.InvalidState)]
    [InlineData("foreign",EchoRestoreOutcome.InvalidEcho)][InlineData("death",EchoRestoreOutcome.StaleDeath)]
    [InlineData("combat",EchoRestoreOutcome.InCombat)][InlineData("trade",EchoRestoreOutcome.Busy)]
    public void RejectedRecoveryCannotConsumeItemsOrChangeDeadState(string reason,EchoRestoreOutcome expected)
    {
        var w=Create();var p=w.AddPlayer(1,new(1),Dead(w,reason=="seal"?0:2));var row=Assert.Single(w.Echoes!.Roster(p.EntityId,0).Entries);
        if(reason=="owner")w.Combat!.PayHealth(p.EntityId,w.Combat.Get(p.EntityId).Health,true);
        if(reason=="combat")w.Combat!.ApplyStatusControl(default,p.EntityId,40,1,Content.Shared.Combat.CrowdControlKind.Root,1);
        if(reason=="trade")Assert.True(w.Inventory!.HoldExchange(p.EntityId,7));
        var before=w.CaptureCharacter(1);Assert.True(w.TryQueueEchoRestore(1,new(1,reason=="foreign"?row.Handle+100:row.Handle,reason=="death"?4ul:row.DeathId)));Step(w);
        Assert.Equal(expected,w.Echoes.RestoreResults[p.EntityId].Outcome);
        Assert.Equal(before.Inventory!.Items,w.CaptureCharacter(1).Inventory!.Items);
        Assert.Equal(SavedEchoAvailability.RequiresRestoration,Assert.Single(w.CaptureCharacter(1).OwnedEchoes!).Availability);
    }

    [Fact]public void DeadBodiesAreRemovedOnlyAfterTheirDeathWasPublishedAndCannotBeRecoveredEarly()
    {
        var w=Create();var initial=Dead(w);var ready=Assert.Single(initial.OwnedEchoes!) with {Availability=SavedEchoAvailability.Ready,Health=110,DeathId=0};
        var p=w.AddPlayer(1,new(1),initial with {OwnedEchoes=[ready]});var body=EchoTests.Summon(w,1);
        w.Combat!.PayHealth(body,w.Combat.Get(body).Health,true);w.Echoes!.Simulate(0,w.Tick);
        var row=Assert.Single(w.Echoes.Roster(p.EntityId,w.Tick).Entries);Assert.Equal(1ul,row.DeathId);
        Assert.True(w.TryQueueEchoRestore(1,new(1,row.Handle,row.DeathId)));w.Echoes.Simulate(0,w.Tick);
        Assert.Equal(EchoRestoreOutcome.Busy,w.Echoes.RestoreResults[p.EntityId].Outcome);Assert.Equal(2,w.Inventory!.Capture(p.EntityId).Items.Length);
        Step(w);Assert.True(w.TryQueueEchoRestore(1,new(2,row.Handle,row.DeathId)));Step(w);
        Assert.Equal(EchoRestoreOutcome.Completed,w.Echoes.RestoreResults[p.EntityId].Outcome);Assert.Single(w.Inventory.Capture(p.EntityId).Items);
    }
    [Fact]public void RecoveryAgesEffectsCooldownsAndAcrThenFillsTheNewMaximaWithoutOfflineTicks()
    {
        var root=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        root["effects"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"old_debuff","durationSeconds":1,"persistThroughDeath":true,"disposition":"Harmful",
             "modifiers":{"maxHealth":-40,"maxMana":-60,"maxStamina":-50}}
            """));
        root["effects"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"remaining_buff","durationSeconds":10,"persistThroughDeath":true,"disposition":"Beneficial","modifiers":{"maxHealth":20},
             "periodic":{"kind":"Healing","intervalSeconds":1,"power":{"flat":2}}}
            """));
        var w=Create(ContentCatalog.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())));var seed=Dead(w);var dead=Assert.Single(seed.OwnedEchoes!);
        var acr=Enum.GetValues<ControlCategory>().Select(category=>new SavedAcrState(category,category==ControlCategory.Hard?2:0,10)).ToArray();
        dead=dead with {CombatControl=dead.CombatControl with {Acr=acr}};
        SavedTimedEffect Effect(string id,double remaining,ulong order)=>new()
        {InstanceId=Guid.NewGuid(),Source=new(CombatSourceKind.Environment,Guid.Empty),TargetEcho=dead.InstanceId,RemainingSeconds=remaining,
            AppliedOrder=order,DefinitionId=id,Stacks=1,EnvironmentActive=true,NextTickSeconds=id=="remaining_buff"?.5:null};
        var p=w.AddPlayer(1,new(1),seed with {OwnedEchoes=[dead],Effects=[Effect("old_debuff",1,1),Effect("remaining_buff",10,2)]});
        w.PvpClock=()=>102000;
        // Inactive owned records age without receiving simulated healing or resource ticks.
        for(var i=0;i<40;i++)Step(w);
        var row=Assert.Single(w.Echoes!.Roster(p.EntityId,w.Tick).Entries);Assert.True(w.TryQueueEchoRestore(1,new(1,row.Handle,row.DeathId)));Step(w);
        var saved=w.CaptureCharacter(1);var ready=Assert.Single(saved.OwnedEchoes!);
        Assert.Equal(270,ready.Health,8);Assert.Equal(140,ready.Mana);Assert.Equal(100,ready.Defense.Stamina);
        Assert.InRange(Assert.Single(ready.Cooldowns).Seconds,4.94,4.96);
        var remaining=Assert.IsType<SavedTimedEffect>(Assert.Single(saved.Effects!));Assert.Equal("remaining_buff",remaining.DefinitionId);
        Assert.InRange(remaining.RemainingSeconds,7.94,7.96);Assert.InRange(remaining.NextTickSeconds!.Value,.44,.46);
        var hard=ready.CombatControl.Acr!.Single(a=>a.Category==ControlCategory.Hard);Assert.Equal(2,hard.Used);Assert.InRange(hard.ResetSeconds,7.94,7.96);
    }
    [Fact]public async Task StaleCheckpointRollsBackTheActualSealDebitAndRecoveredEchoTogether()
    {
        var w=Create();var initial=Dead(w);var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var first=await store.OpenAsync("",initial,default);var token=first.IssuedToken;
        await store.SaveAsync([new(first,initial)],default);first.Revision++;
        var second=await store.OpenAsync("",w.CreateInitialCharacter(),default);
        await store.SaveAsync([new(second,second.State)],default); // Deliberately leave the session's fence stale.
        var p=w.AddPlayer(1,new(1),initial);var row=Assert.Single(w.Echoes!.Roster(p.EntityId,w.Tick).Entries);
        Assert.True(w.TryQueueEchoRestore(1,new(1,row.Handle,row.DeathId)));Step(w);var recovered=w.CaptureCharacter(1);
        Assert.Single(recovered.Inventory!.Items);Assert.Equal(SavedEchoAvailability.Ready,Assert.Single(recovered.OwnedEchoes!).Availability);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveAsync([new(first,recovered),new(second,second.State)],default));
        await first.DisposeAsync();await second.DisposeAsync();
        var restored=await store.OpenAsync(token,w.CreateInitialCharacter(),default);
        Assert.Equal(2,restored.State.Inventory!.Items.Length);Assert.Equal(SavedEchoAvailability.RequiresRestoration,Assert.Single(restored.State.OwnedEchoes!).Availability);
        await store.SaveAsync([new(restored,recovered)],default);await restored.DisposeAsync();
        await using var committed=await store.OpenAsync(token,w.CreateInitialCharacter(),default);
        Assert.Single(committed.State.Inventory!.Items);Assert.Equal(SavedEchoAvailability.Ready,Assert.Single(committed.State.OwnedEchoes!).Availability);
    }
}
