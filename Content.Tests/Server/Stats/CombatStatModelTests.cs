using Content.Server.Stats;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Stats;

public sealed class CombatStatModelTests
{
    [Fact]
    public void WisdomOwnsManaAndMagicDefenseWhileIntelligenceOnlyAddsMagicAttack()
    {
        var c = new StatCalculator(ContentCatalogTests.Load().Balance);
        var baseline = c.Calculate(new(10,10,10,10,10,10,10),100,60,1,1);
        var intelligence = c.Calculate(new(10,10,10,20,10,10,10),100,60,1,1);
        var wisdom = c.Calculate(new(10,10,10,10,10,10,20),100,60,1,1);
        Assert.Equal(140,baseline.MaxMana); Assert.Equal(3,baseline.ManaRecovery); Assert.Equal(15,baseline.MagicDefense);
        Assert.Equal(baseline.MaxMana,intelligence.MaxMana); Assert.Equal(baseline.ManaRecovery,intelligence.ManaRecovery);
        Assert.Equal(baseline.MagicDefense,intelligence.MagicDefense); Assert.Equal(baseline.MagicAttack+30,intelligence.MagicAttack);
        Assert.Equal(baseline.MagicAttack,wisdom.MagicAttack); Assert.Equal(220,wisdom.MaxMana);
        Assert.Equal(5,wisdom.ManaRecovery); Assert.Equal(30,wisdom.MagicDefense);
    }

    [Fact]
    public void AccuracyEvasionAndCriticalDamageHaveApprovedIndependentCoefficients()
    {
        var c = new StatCalculator(ContentCatalogTests.Load().Balance);
        var stats = c.Calculate(new(0,35,0,0,100,0,0),100,60,0,0,
            new DerivedStatModifiers { Accuracy=-10,Evasion=15,CriticalDamage=.2 });
        Assert.Equal(90,stats.Accuracy); Assert.Equal(50,stats.Evasion); Assert.Equal(2.7,stats.CriticalDamage,10);
        Assert.Equal(.05,stats.CriticalChance,10);
    }

    [Theory]
    [InlineData(-100,0,.874839002001335)]
    [InlineData(0,0,.95)]
    [InlineData(100,0,.981005693354)]
    public void ApprovedHitCurveHasNoExtraBaselineOrFloor(double accuracy,double evasion,double expected)
        => Assert.Equal(expected,StatCalculator.HitProbability(accuracy,evasion),6);

    [Fact]
    public void HitCurveRemainsFiniteAndMonotonicAtSignedExtremes()
    {
        Assert.Equal(0,StatCalculator.HitProbability(-double.MaxValue,double.MaxValue));
        Assert.Equal(1,StatCalculator.HitProbability(double.MaxValue,-double.MaxValue));
        Assert.Equal(.95,StatCalculator.HitProbability(double.MaxValue,double.MaxValue),10);
        Assert.Throws<ArgumentException>(()=>StatCalculator.HitProbability(double.NaN,0));
        Assert.Throws<ArgumentException>(()=>new BaseStats(0,0,0,0,0,0,-1));
    }
}
