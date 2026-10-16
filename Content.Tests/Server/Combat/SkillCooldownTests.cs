using Content.Server.Combat;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class SkillCooldownTests
{
    [Fact]
    public void SpendingAnotherChargeNeverRestartsSequentialRecovery()
    {
        var slot=new SkillCooldown(3,5);
        Assert.True(slot.Consume(0)); Assert.True(slot.Consume(2));
        Assert.Equal(new SkillCooldownState(1,3,3,5),slot.State(2));
        Assert.Equal(new SkillCooldownState(2,3,5,5),slot.State(5));
        Assert.Equal(new SkillCooldownState(3,3,0,0),slot.State(10));
    }

    [Fact]
    public void ChangedCdrKeepsRunningIntervalAndChangesOnlyLaterIntervals()
    {
        var slot=new SkillCooldown(3,5); slot.Consume(0); slot.Consume(1); slot.Configure(2,2);
        Assert.Equal(3,slot.State(2).RechargeIn); Assert.Equal(5,slot.State(2).RechargeDuration);
        Assert.Equal(new SkillCooldownState(2,3,2,2),slot.State(5));
        Assert.Equal(3,slot.State(7).Available);
    }

    [Fact]
    public void OrdinaryResetRestoresEveryChargeAndCancelsOldTimer()
    {
        var slot=new SkillCooldown(3,5); slot.Consume(0); slot.Consume(1); slot.Reset(2);
        Assert.Equal(new SkillCooldownState(3,3,0,0),slot.State(2));
        slot.Consume(3); Assert.Equal(1,slot.State(7).RechargeIn);
        Assert.Equal(3,slot.State(8).Available);
    }

    [Fact]
    public void FullCdrHasZeroCooldownButDoesNotCompleteOldIntervalEarly()
    {
        var slot=new SkillCooldown(3,5); slot.Consume(0); slot.Configure(0,1); slot.Consume(1); slot.Consume(2);
        Assert.False(slot.Consume(4)); Assert.Equal(0,slot.State(4).Available);
        Assert.Equal(3,slot.State(5).Available);
        Assert.True(slot.Consume(5)); Assert.Equal(3,slot.State(5).Available);
    }

    [Fact]
    public void OfflineRecoveryProcessesMoreThanTheFirstCharge()
    {
        var slot=new SkillCooldown(3,5); slot.Restore(0,2,5,0,8);
        Assert.Equal(new SkillCooldownState(2,3,4,5),slot.State(0));
        Assert.Equal(3,slot.State(4).Available);
    }

    [Fact]
    public void EquipmentCopyPreservesAvailableChargesAndCurrentTimer()
    {
        var slot=new SkillCooldown(2,10); slot.Consume(0); var next=slot.Copy(1,4);
        Assert.Equal(new SkillCooldownState(1,2,6,10),next.State(4));
        Assert.Equal(10,slot.ConfiguredDuration); Assert.Equal(1,next.ConfiguredDuration);
    }

    [Fact]public void ChannelReservationConsumesAvailabilityWithoutStartingItsInterval()
    {
        var slot=new SkillCooldown(1,5);Assert.True(slot.ReserveUntilEnd(0));Assert.False(slot.Recovering);
        Assert.Equal(new SkillCooldownState(0,1,0,0,1),slot.State(100));Assert.False(slot.Consume(100));
        Assert.True(slot.FinishReservation(100));Assert.Equal(new SkillCooldownState(0,1,5,5),slot.State(100));
        Assert.Equal(1,slot.State(105).Available);
    }

    [Fact]public void ExistingRecoveryRunsWhileChannelChargeWaitsAndIsNotRestartedAtEnd()
    {
        var slot=new SkillCooldown(3,5);slot.Consume(0);slot.ReserveUntilEnd(1);
        Assert.Equal(new SkillCooldownState(1,3,3,5,1),slot.State(2));slot.FinishReservation(2);
        Assert.Equal(new SkillCooldownState(1,3,3,5),slot.State(2));Assert.Equal(2,slot.State(5).Available);
        Assert.Equal(3,slot.State(10).Available);
    }

    [Fact]public void ChannelReservationSurvivesEquipmentCopyButFullResetRestoresIt()
    {
        var slot=new SkillCooldown(2,5);slot.ReserveUntilEnd(0);var next=slot.Copy(2,100);
        Assert.Equal(new SkillCooldownState(1,2,0,0,1),next.State(100));next.FinishReservation(100);
        Assert.Equal(2,next.State(102).Available);slot.Reset(101);Assert.False(slot.FinishReservation(102));
        Assert.Equal(new SkillCooldownState(2,2,0,0),slot.State(102));
    }

    [Fact]public void DeferredCooldownRestartsAtLoginWithoutOfflineCredit()
    {
        var slot=new SkillCooldown(1,5);slot.Restore(0,0,0,10,1000,pendingEnd:1);
        Assert.Equal(new SkillCooldownState(0,1,5,5),slot.State(10));Assert.Equal(1,slot.State(15).Available);
        var sequential=new SkillCooldown(3,5);sequential.Restore(0,2,5,10,1000,pendingEnd:1);
        Assert.Equal(new SkillCooldownState(2,3,5,5),sequential.State(10));
    }
}
