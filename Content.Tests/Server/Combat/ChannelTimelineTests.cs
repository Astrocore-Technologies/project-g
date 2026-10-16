using Content.Server.Combat;
using Content.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ChannelTimelineTests
{
    internal static ChannelDefinition Definition=>new(){MaximumSeconds=1,IntervalSeconds=.1,IntervalSpeed=ActionSpeedSource.None,
        PulseAtStart=false,ManaPerSecond=3,StaminaPerSecond=2,CooldownStart=ChannelCooldownStart.Activation,DamageCalculation=EffectCalculationMode.Dynamic};

    [Theory][InlineData(.05)][InlineData(.1)][InlineData(.25)][InlineData(1)]
    public void PaidTimeAndEndpointPulseCountAreIndependentOfServerStep(double delta)
    {
        var channel=new ChannelTimeline(Definition,0);var mana=10d;var stamina=10d;var pulses=0;var paid=0d;
        for(var step=1;!channel.Finished;step++)
        {var result=channel.Advance(Math.Min(1,step*delta),mana,stamina);mana-=result.Mana;stamina-=result.Stamina;pulses+=result.Pulses;paid+=result.PaidSeconds;}
        Assert.Equal(10,pulses);Assert.Equal(1,paid,12);Assert.Equal(7,mana,12);Assert.Equal(8,stamina,12);
    }

    [Fact]public void FractionalManaFundsOnlyItsPartOfTheStepAndNeverTheNextPulse()
    {
        var channel=new ChannelTimeline(Definition,0);var result=channel.Advance(1,.75,10);
        Assert.Equal(.25,result.PaidSeconds,12);Assert.Equal(.75,result.Mana,12);Assert.Equal(.5,result.Stamina,12);
        Assert.Equal(2,result.Pulses);Assert.True(result.Starved);Assert.True(result.Finished);
        Assert.Equal(0,channel.Advance(2,100,100).Pulses);
    }

    [Fact]public void BothRatesLimitTimeAndAnExactlyFundedEndpointStillFires()
    {
        var channel=new ChannelTimeline(Definition,0);var result=channel.Advance(1,100,.6);
        Assert.Equal(.3,result.PaidSeconds,12);Assert.Equal(.9,result.Mana,12);Assert.Equal(.6,result.Stamina,12);Assert.Equal(3,result.Pulses);
    }

    [Fact]public void UnfundedLaterTimeNeverCreatesWorkAndResourcesAreNotRoundedToIntegers()
    {
        var channel=new ChannelTimeline(Definition,10);var first=channel.Advance(10.05,1,1);
        Assert.Equal(.15,first.Mana,12);Assert.Equal(.1,first.Stamina,12);Assert.Equal(0,first.Pulses);
        var second=channel.Advance(11,0,1);Assert.True(second.Starved);Assert.Equal(0,second.PaidSeconds);Assert.Equal(0,second.Pulses);
    }

    [Fact]public void AnExplicitActivationPulseIsConsumedOnceAndAnUnboundedFreeChannelStaysActive()
    {
        var channel=new ChannelTimeline(Definition with {MaximumSeconds=null,PulseAtStart=true,ManaPerSecond=0,StaminaPerSecond=0},0);
        Assert.Equal(1,channel.Advance(0,0,0).Pulses);Assert.Equal(0,channel.Advance(0,0,0).Pulses);
        Assert.Equal(10,channel.Advance(1,0,0).Pulses);Assert.False(channel.Finished);Assert.Null(channel.Remaining);
    }

    [Theory][InlineData(.29)][InlineData(.7)][InlineData(3.141592653589793)]
    public void TheLimitingResourceIsExhaustedExactlyAndReportsItsOwnReason(double mana)
    {
        var channel=new ChannelTimeline(Definition with {ManaPerSecond=100000},0);var step=channel.Advance(1,mana,100);
        Assert.True(step.Starved);Assert.True(step.ManaLimited);Assert.Equal(mana,step.Mana);Assert.Equal(0,mana-step.Mana);
        Assert.Equal(0,step.Pulses);
    }
}
