using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ControlFeedbackTests
{
    private sealed class Fight
    {
        internal readonly ServerWorld World;
        internal CombatSimulation Combat=>World.Combat!;
        internal readonly NetworkEntityId Source,Other,Target;
        internal Fight(CrowdControlKind? immune=null)
        {
            var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
            if(immune is {} kind)data["creatures"]!.AsArray().Single(c=>c!["id"]!.GetValue<string>()=="test_adventurer")!["controlResistance"]=
                new JsonObject{["immunities"]=new JsonArray(JsonValue.Create(kind.ToString()))};
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
                catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
            Source=World.AddPlayer(1,new(1),World.CreateInitialCharacter() with {X=-7,Z=0}).EntityId;
            Other=World.AddPlayer(2,new(2),World.CreateInitialCharacter() with {X=-6,Z=0}).EntityId;
            Target=World.AddPlayer(3,new(3),World.CreateInitialCharacter() with {X=-5,Z=0}).EntityId;
            Combat.DamagePermission=(_,_)=>true;
        }
        internal bool Apply(CrowdControlKind kind= CrowdControlKind.Root,double duration=2,ulong execution=1,NetworkEntityId? source=null)
            =>Combat.ApplyStatusControl(source??Source,Target,10,execution,kind,duration);
    }

    [Fact]public void AcceptedRefreshReportsActualDurationAndNeverDuplicatesOneExecution()
    {
        var f=new Fight();Assert.True(f.Apply());var first=Assert.Single(f.Combat.ControlFeedbackEvents);
        Assert.Equal(ControlChangeKind.Applied,first.Change);Assert.Equal(1u,first.RemainingInstances);Assert.Equal(2,first.Duration);
        f.Combat.Simulate(1,1);Assert.Empty(f.Combat.ControlFeedbackEvents);
        Assert.True(f.Apply(execution:2));var refreshed=Assert.Single(f.Combat.ControlFeedbackEvents);
        Assert.Equal(ControlChangeKind.Refreshed,refreshed.Change);Assert.Equal(first.Source,refreshed.Source);
        Assert.Equal(f.Combat.Get(f.Target).Controls.LastEnd(CrowdControlKind.Root)-f.Combat.Time,refreshed.Duration,12);
        Assert.Equal(1u,refreshed.RemainingInstances);Assert.True(refreshed.Id>first.Id);
        Assert.False(f.Apply(execution:2));Assert.Single(f.Combat.ControlFeedbackEvents);
    }

    [Fact]public void ExpiringOneSourceDoesNotClaimThatAnOverlappingSourceHasEnded()
    {
        var f=new Fight();Assert.True(f.Apply(duration:.125));Assert.True(f.Apply(duration:1,execution:2,source:f.Other));
        Assert.Equal(new[]{1u,2u},f.Combat.ControlFeedbackEvents.Select(e=>e.RemainingInstances));
        f.Combat.Simulate(.125f,1);var expired=Assert.Single(f.Combat.ControlFeedbackEvents);
        Assert.Equal(ControlChangeKind.Expired,expired.Change);Assert.Equal(1u,expired.RemainingInstances);
        Assert.Equal(0,expired.Duration);Assert.Equal(0,expired.Strength);Assert.True(f.Combat.Get(f.Target).Controls.Has(CrowdControlKind.Root));
        Assert.True(f.Combat.RemoveControl(f.Target,CrowdControlKind.Root));var removed=f.Combat.ControlFeedbackEvents[^1];
        Assert.Equal(ControlChangeKind.Removed,removed.Change);Assert.Equal(0u,removed.RemainingInstances);
        Assert.False(f.Combat.RemoveControl(f.Target,CrowdControlKind.Root));Assert.Equal(2,f.Combat.ControlFeedbackEvents.Count);
    }

    [Fact]public void RejectionAndReplayedExecutionDoNotInventSuccessfulApplicationFeedback()
    {
        var f=new Fight();Assert.True(f.Apply(CrowdControlKind.Stun,6));
        Assert.False(f.Apply(CrowdControlKind.Stun,1,2));Assert.Equal(2,f.Combat.ControlFeedbackEvents.Count);
        Assert.Equal(ControlMitigation.AdaptiveResistance,f.Combat.ControlFeedbackEvents[^1].Mitigation);
        Assert.Equal(ControlChangeKind.Rejected,f.Combat.ControlFeedbackEvents[^1].Change);
        Assert.False(f.Combat.ApplyStatusControl(f.Source,f.World.TrainingTargetId,10,3,CrowdControlKind.Stun,1));
        Assert.Equal(3,f.Combat.ControlFeedbackEvents.Count);Assert.Equal(ControlMitigation.Immunity,f.Combat.ControlFeedbackEvents[^1].Mitigation);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ImmunityAndFullResistanceReportOnceWithoutChangingControlBudgetRevisionOrJournal(bool immune)
    {
        var f=new Fight(immune ? CrowdControlKind.Root : null);var actor=f.Combat.Get(f.Target);
        if(!immune)actor.ControlResistance.Categories[ControlCategory.Movement]=1;
        var revision=actor.Controls.Revision;var before=actor.Controls.Ledger.Snapshot(ControlCategory.Movement,0);
        Assert.False(f.Apply());Assert.False(f.Apply());var feedback=Assert.Single(f.Combat.ControlFeedbackEvents);
        Assert.Equal(ControlChangeKind.Rejected,feedback.Change);
        Assert.Equal(immune ? ControlMitigation.Immunity : ControlMitigation.Resistance,feedback.Mitigation);
        Assert.Equal(0,feedback.Duration);Assert.Equal(0,feedback.Strength);Assert.Equal(0u,feedback.RemainingInstances);
        Assert.Equal(revision,actor.Controls.Revision);Assert.Equal(before,actor.Controls.Ledger.Snapshot(ControlCategory.Movement,0));
        Assert.Empty(actor.Controls.Active);Assert.Empty(f.Combat.CurrentJournal(f.Target));
    }

    [Fact]public void PartialResistanceAndAcrAreReportedOnTheActualSuccessfulExposure()
    {
        var f=new Fight();var actor=f.Combat.Get(f.Target);
        actor.ControlResistance.Types[CrowdControlKind.Root]=.25;actor.ControlResistance.Categories[ControlCategory.Movement]=.5;
        Assert.True(f.Apply());var first=Assert.Single(f.Combat.ControlFeedbackEvents);
        Assert.Equal(.75,first.Duration);Assert.Equal(ControlMitigation.Resistance,first.Mitigation);
        Assert.True(f.Apply(execution:2,source:f.Other));var second=f.Combat.ControlFeedbackEvents[^1];
        Assert.Equal(ControlMitigation.Resistance|ControlMitigation.AdaptiveResistance,second.Mitigation);Assert.True(second.Duration<first.Duration);
        Assert.Equal(second.Duration,actor.Controls.Active.Single(c=>c.Key.Source==second.Source).EndsAt-f.Combat.Time);
    }

    [Fact]public void RejectedSpamDoesNotExtendAcrResetAndAnUnneededRefreshIsSilent()
    {
        var f=new Fight();Assert.True(f.Apply(CrowdControlKind.Stun,2));Assert.False(f.Apply(CrowdControlKind.Stun,1,2));
        Assert.Single(f.Combat.ControlFeedbackEvents); // Existing time already covers the request; this is not immunity.
        Assert.True(f.Combat.RemoveControl(f.Target,CrowdControlKind.Stun));
        Assert.True(f.Apply(CrowdControlKind.Stun,20,3)); // Fill the remaining Hard budget.
        var before=f.Combat.Get(f.Target).Controls.Ledger.Snapshot(ControlCategory.Hard,f.Combat.Time);
        Assert.False(f.Apply(CrowdControlKind.Stun,1,4));Assert.False(f.Apply(CrowdControlKind.Stun,1,4));
        Assert.Single(f.Combat.ControlFeedbackEvents,c=>c.Change==ControlChangeKind.Rejected);
        Assert.Equal(before,f.Combat.Get(f.Target).Controls.Ledger.Snapshot(ControlCategory.Hard,f.Combat.Time));
        f.Combat.Simulate((float)before.ResetInSeconds,1);Assert.True(f.Apply(CrowdControlKind.Stun,1,5));
        Assert.Equal(ControlMitigation.None,f.Combat.ControlFeedbackEvents[^1].Mitigation);
    }

    [Theory][InlineData(CrowdControlKind.Knockup)][InlineData(CrowdControlKind.Knockback)][InlineData(CrowdControlKind.Pull)]
    public void ForcedControlImmunityUsesTheSameConfirmedRejection(CrowdControlKind kind)
    {
        var f=new Fight(kind);var start=f.Combat.Get(f.Target).Foot;
        var accepted=kind switch
        {
            CrowdControlKind.Knockup=>f.Combat.Knockup(f.Target,1,1,1,f.Source,10,42),
            CrowdControlKind.Pull=>f.Combat.Pull(f.Target,new(-7,0),1,2,f.Source,10,42),
            _=>f.Combat.Knockback(f.Target,Vector2.UnitX,1,2,f.Source,10,42)
        };
        Assert.False(accepted);var feedback=Assert.Single(f.Combat.ControlFeedbackEvents);
        Assert.Equal(kind,feedback.Control);Assert.Equal(ControlMitigation.Immunity,feedback.Mitigation);
        Assert.Equal(start,f.Combat.Get(f.Target).Foot);Assert.False(f.Combat.Get(f.Target).Controls.HasActive);
    }

    [Fact]public void SleepWakeAndDeathUseTheSameRemovalObserverWithoutResettingAcr()
    {
        var f=new Fight();Assert.True(f.Apply(CrowdControlKind.Sleep,2));
        var budget=f.Combat.Get(f.Target).Controls.Ledger.Snapshot(ControlCategory.Hard,0).Used;
        Assert.True(f.Combat.ResolveDirectHit(f.Source,f.Target,Vector2.UnitX,1,false).Damage>0);
        var wake=f.Combat.ControlFeedbackEvents[^1];Assert.Equal(CrowdControlKind.Sleep,wake.Control);Assert.Equal(ControlChangeKind.Removed,wake.Change);
        Assert.Equal(budget,f.Combat.Get(f.Target).Controls.Ledger.Snapshot(ControlCategory.Hard,0).Used);
        Assert.True(f.Apply(CrowdControlKind.Root,2,2));Assert.True(f.Combat.PayHealth(f.Target,f.Combat.Get(f.Target).Health,true));
        var death=f.Combat.ControlFeedbackEvents[^1];Assert.Equal(CrowdControlKind.Root,death.Control);Assert.Equal(ControlChangeKind.Removed,death.Change);
        Assert.Equal(0u,death.RemainingInstances);Assert.False(f.Combat.Get(f.Target).Controls.HasActive);
    }

    [Fact]public void QuickRecoveryReportsRemovalOfTheCombinedLaunchAndProneControl()
    {
        var f=new Fight();Assert.True(f.Combat.Knockup(f.Target,.125,1,1,f.Source));f.Combat.Simulate(.125f,1);
        Assert.Equal(CombatControlPhase.KnockedDown,f.Combat.Get(f.Target).Control);
        Assert.Equal(DefenseOutcome.Accepted,f.Combat.QuickRecover(f.Target,new(1,DefenseAction.QuickRecover,Vector2.UnitY)));
        var removed=Assert.Single(f.Combat.ControlFeedbackEvents);Assert.Equal(CrowdControlKind.Knockup,removed.Control);
        Assert.Equal(ControlChangeKind.Removed,removed.Change);
    }

    [Fact]public void RestoringPersistedInstancesDoesNotReplayHistoricalApplicationFeedback()
    {
        var f=new Fight();Assert.True(f.Apply());var state=f.World.CaptureCharacter(3);f.World.RemovePlayer(3);
        f.Combat.Simulate(.05f,1);f.World.AddPlayer(3,new(3),state);
        Assert.Empty(f.Combat.ControlFeedbackEvents);
    }
}
