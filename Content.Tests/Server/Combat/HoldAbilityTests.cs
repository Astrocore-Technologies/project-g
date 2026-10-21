using System.Numerics;
using System.Text.Json.Nodes;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Server.World;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class HoldAbilityTests
{
    private static JsonNode Document(bool preparing=false)
    {
        var data=ChannelingTests.Document();var skill=data["abilities"]![1]!;
        skill["action"]!["form"]="Hold";skill["action"]!["preparation"]!["cancel"]="Voluntary, Skill, Block, Parry, Dodge";
        if(preparing)skill["action"]!["preparation"]!["seconds"]=.5;
        skill["channel"]!["maximumSeconds"]=null;return data;
    }
    private static void Control(ChannelingTests.Fight f,AbilityControlKind kind=AbilityControlKind.Release,Vector2? aim=null)=>
        Assert.True(f.World.TryQueueAbilityControl(42,new(1,1,f.World.Tick,kind,aim??f.Combat.Get(f.World.TrainingTargetId).Position)));

    [Fact]public void HoldPaysForWorkUntilReleaseAndStartsTheReservedCooldownWithoutAnotherPrice()
    {
        var f=new ChannelingTests.Fight(Document());f.Cast();f.Step();f.Step();Assert.Single(f.Hits);var mana=f.Mana;
        Assert.Equal(1,f.Skill.PendingEndCharges);Control(f);f.Step();
        Assert.Equal(mana,f.Mana);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Equal(5,f.Skill.ReadyInSeconds);Assert.Empty(f.Abilities.ActiveStates);
        for(var i=0;i<10;i++)f.Step();Assert.Single(f.Hits);Assert.Equal(mana,f.Mana);
    }

    [Fact]public void ReleaseBeforeActivationCancelsPreparationWithoutAChargeOrResourcePrice()
    {
        var f=new ChannelingTests.Fight(Document(true));f.Cast();Control(f);f.Step();
        Assert.Equal(100,f.Mana);Assert.Equal(1,f.Skill.Charges);Assert.Equal(0,f.Skill.ReadyInSeconds);Assert.Empty(f.Hits);Assert.Null(f.Actor.Action);
    }

    [Fact]public void APressAndReleaseWithinOneTickPaysOnlyActivationAndCreatesNoUnpaidPulse()
    {
        var f=new ChannelingTests.Fight(Document());Assert.True(f.World.TryQueueAbility(42,new(1,0,2,f.Combat.Get(f.World.TrainingTargetId).Position),0));
        Control(f);f.Step();Assert.Equal(98,f.Mana);Assert.Empty(f.Hits);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Equal(5,f.Skill.ReadyInSeconds);Assert.Null(f.Actor.Action);
    }

    [Theory][InlineData(true,0)][InlineData(false,1)]
    public void HeldAimUpdatesUseActiveTurningPermission(bool turn,int expectedHits)
    {
        var data=Document();data["abilities"]![1]!["action"]!["active"]!["canTurn"]=turn;
        var f=new ChannelingTests.Fight(data);f.Cast();Control(f,AbilityControlKind.Aim,f.Actor.Position+new Vector2(3,0));f.Step();f.Step();
        Assert.Equal(expectedHits,f.Hits.Count);Assert.Equal(turn,f.Skill.SustainCanTurn);
    }

    [Fact]public void AnUnrelatedReleaseCannotStopTheCurrentHeldExecution()
    {
        var f=new ChannelingTests.Fight(Document());f.Cast();
        Assert.True(f.World.TryQueueAbilityControl(42,new(1,2,f.World.Tick,AbilityControlKind.Release,f.Actor.Position)));
        f.Step();Assert.True(f.Actor.IsCasting);Assert.Equal(1,f.Skill.PendingEndCharges);
    }

    [Fact]public void HoldEndsAtTheFundedResourceBoundaryWithoutHalfPenalty()
    {
        var f=new ChannelingTests.Fight(Document(),mana:2.75,delta:.1f);f.Cast();f.Finish();
        Assert.Equal(0,f.Mana);Assert.Equal(2,f.Hits.Count);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Equal(AbilityOutcome.NoMana,f.Abilities.Results[f.Id].Outcome);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void InterruptionAndDeathStopWorkAndStartCompletionCooldown(bool death)
    {
        var f=new ChannelingTests.Fight(Document());f.Cast();f.Step();var mana=f.Mana;
        if(death)f.Combat.ApplyAbilityDamage(f.Id,100000,f.World.TrainingTargetId);else f.Abilities.InterruptCast(f.Id);
        f.Step();Assert.Equal(mana,f.Mana);Assert.Equal(0,f.Skill.PendingEndCharges);Assert.InRange(f.Skill.ReadyInSeconds,4.9,5);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Theory][InlineData("preparation")][InlineData("active")]
    public void HoldCannotForbidItsOwnReleaseInAuthoredCancellationWindows(string phase)
    {
        var data=Document();data["abilities"]![1]!["action"]![phase]!["cancel"]="None";
        Assert.Throws<InvalidDataException>(()=>ChannelingTests.Parse(data));
    }

    [Fact]public void RepeatedAimSnapshotsUseTheUnreliableLaneWithoutDuplicatingReliableLifecycle()
    {
        var f=new ChannelingTests.Fight(Document());f.Cast();var view=new AbilityInterestView();
        f.Abilities.UpdateInterest(view,[f.Id],f.Actor.Position,20,25,f.World.Tick);Assert.Single(view.Changes);Assert.Empty(view.Sustained);
        Control(f,AbilityControlKind.Aim,f.Actor.Position+new Vector2(3,0));f.Step();
        f.Abilities.UpdateInterest(view,[f.Id],f.Actor.Position,20,25,f.World.Tick);Assert.Empty(view.Changes);Assert.Single(view.Sustained);
        Assert.Equal(f.Actor.Position+new Vector2(3,0),view.Sustained[0].Position);
    }
}
