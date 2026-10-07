using Content.Server.Data;
using Content.Server.Stats;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Stats;

public sealed class StatCalculatorTests
{
    private readonly ContentCatalog _catalog = ContentCatalogTests.Load();
    private StatCalculator Calculator => new(_catalog.Balance);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void EveryPrimaryStatRejectsNegatives(int index)
    {
        double[] values = [0, 0, 0, 0, 0, 0];
        values[index] = -1;
        Assert.Throws<ArgumentException>(() => new BaseStats(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFinitePrimaryStatsAreRejected(double value) =>
        Assert.Throws<ArgumentException>(() => new BaseStats(value, 0, 0, 0, 0, 0));

    [Fact]
    public void PrototypeRagnarokContributionsAreFractionalAndDataDriven()
    {
        var result = Calculator.Calculate(new BaseStats(10, 10, 10, 10, 10, 10), 100, 50, 1, 1);
        Assert.Equal(10 + 2 + 10d / 3, result.MeleeAttack, 10);
        Assert.Equal(result.MeleeAttack, result.RangedAttack);
        Assert.Equal(15 + 2 + 10d / 3, result.MagicAttack, 10);
        Assert.Equal(1.05, result.MeleeWeaponMultiplier, 10);
        Assert.Equal(7, result.PhysicalDefense, 10);
        Assert.Equal(14, result.MagicDefense, 10);
        Assert.Equal(110, result.MaxHealth, 10);
        Assert.Equal(55, result.MaxMana, 10);
        Assert.Equal(1 + 10d / 30, result.HealthRecovery, 10);
        Assert.Equal(1 + 10d / 36, result.ManaRecovery, 10);
        Assert.Equal(1.2, result.HealthItemMultiplier, 10);
        Assert.Equal(1.1, result.ManaItemMultiplier, 10);
        Assert.Equal(1.125, result.AttackSpeedMultiplier, 10);
        Assert.Equal(1.15, result.CastSpeedMultiplier, 10);

        var custom = new StatCalculator(_catalog.Balance with { MeleeAttack = new StatWeights { Strength = 2 } });
        Assert.Equal(20, custom.Calculate(new BaseStats(10, 0, 0, 0, 0, 0), 100, 50, 0, 0).MeleeAttack);
    }

    [Fact]
    public void ZeroStatsHaveValidBaselinesAndNoArtificialMinimumPrimaryStat()
    {
        var value = Calculator.Calculate(default, 100, 50, 0, 0);
        Assert.Equal(100, value.MaxHealth);
        Assert.Equal(50, value.MaxMana);
        Assert.Equal(0, value.MeleeAttack);
        Assert.Equal(0, value.HealthRecovery);
        Assert.Equal(0.05, value.CriticalChance, 10);
        Assert.Equal(1, Calculator.AttackInterval(1, value));
        Assert.Equal(0, Calculator.CastDuration(0, value));
    }

    [Fact]
    public void DerivedStatsCanBeNegativeExceptHealth()
    {
        var value = Calculator.Calculate(default, 100, 50, 1, 1, new DerivedStatModifiers
        {
            MeleeAttack = -100, RangedAttack = -100, MagicAttack = -100,
            PhysicalDefense = -100, MagicDefense = -100, MaxHealth = -200,
            MaxMana = -100, HealthRecovery = -2, ManaRecovery = -2,
            AttackSpeedMultiplier = -2, CastSpeedMultiplier = -2,
            HealthItemMultiplier = -2, ManaItemMultiplier = -2,
            MeleeWeaponMultiplier = -2, RangedWeaponMultiplier = -2, CriticalChance = -1
        });
        foreach (var property in typeof(DerivedStats).GetProperties())
        {
            var stat = (double)property.GetValue(value)!;
            Assert.True(double.IsFinite(stat));
            Assert.True(property.Name == nameof(DerivedStats.MaxHealth) ? stat > 0 : stat < 0);
        }
        Assert.Equal(0, Calculator.WeaponPower(_catalog.Weapons["training_sword"], value));
        Assert.True(Calculator.AttackInterval(1, value) > 1);
        Assert.True(Calculator.CastDuration(1, value) > 1);
        Assert.Equal(0, StatCalculator.CriticalProbability(value));
    }

    [Fact]
    public void DefenseHasContinuousSignedBehaviorWithoutDamageInversion()
    {
        Assert.Equal(100, Calculator.ApplyDefense(100, 0));
        Assert.Equal(50, Calculator.ApplyDefense(100, 100));
        Assert.Equal(200, Calculator.ApplyDefense(100, -100));
        Assert.Equal(0, Calculator.ApplyDefense(0, -double.MaxValue));
        Assert.True(Calculator.ApplyDefense(100, double.MaxValue) >= 0);
        Assert.True(double.IsFinite(Calculator.ApplyDefense(double.MaxValue, -double.MaxValue)));
        Assert.Throws<ArgumentException>(() => Calculator.ApplyDefense(-1, 0));
        Assert.Throws<ArgumentException>(() => Calculator.ApplyDefense(1, double.NaN));
    }

    [Fact]
    public void MaximumFiniteInputsDoNotProduceNaNOrInfinity()
    {
        var maximum = double.MaxValue;
        var stats = new BaseStats(maximum, maximum, maximum, maximum, maximum, maximum);
        var result = Calculator.Calculate(stats, maximum, maximum, maximum, maximum);
        AssertFinite(result);
        Assert.True(result.MaxHealth > 0);
        Assert.True(result.CriticalChance is >= 0 and <= 1);
        Assert.True(double.IsFinite(Calculator.WeaponPower(_catalog.Weapons["training_bow"], result)));
        Assert.True(Calculator.AttackInterval(1, result) > 0);
        Assert.True(Calculator.CastDuration(1, result) > 0);

        var negative = new DerivedStatModifiers
        {
            MaxHealth = -maximum, MaxMana = -maximum, PhysicalDefense = -maximum,
            AttackSpeedMultiplier = -maximum, CastSpeedMultiplier = -maximum,
            CriticalChance = -maximum, MeleeAttack = -maximum
        };
        var debuffed = Calculator.Calculate(default, 1, 1, 0, 0, negative);
        AssertFinite(debuffed);
        Assert.True(double.IsFinite(Calculator.AttackInterval(maximum, debuffed)));
        Assert.True(double.IsFinite(Calculator.CastDuration(maximum, debuffed)));
    }

    [Fact]
    public void CurvesAreMonotonicAndContinuousNearZero()
    {
        var previousCrit = -1d;
        var previousDefenseDamage = double.MaxValue;
        for (var value = 0; value <= 1000; value++)
        {
            var stats = Calculator.Calculate(new BaseStats(0, 0, 0, 0, 0, value), 100, 50, 1, 1);
            Assert.True(stats.CriticalChance >= previousCrit && stats.CriticalChance < 1);
            previousCrit = stats.CriticalChance;
            var damage = Calculator.ApplyDefense(100, value);
            Assert.True(damage <= previousDefenseDamage);
            previousDefenseDamage = damage;
        }
        Assert.InRange(StatMath.PositiveScale(-0.000001), 0.999998, 1);
        Assert.InRange(StatMath.PositiveScale(0.000001), 1, 1.000002);
        Assert.InRange(Calculator.ApplyDefense(100, -0.000001), 100, 100.000002);
        Assert.InRange(Calculator.ApplyDefense(100, 0.000001), 99.999998, 100);
    }

    [Fact]
    public void InvalidCalculationInputsFailBeforeUse()
    {
        Assert.Throws<ArgumentException>(() => Calculator.Calculate(default, 0, 1, 0, 0));
        Assert.Throws<ArgumentException>(() => Calculator.Calculate(default, 1, double.NaN, 0, 0));
        Assert.Throws<ArgumentException>(() => Calculator.Calculate(default, 1, 1, -1, 0));
        Assert.Throws<ArgumentException>(() => Calculator.Calculate(default, 1, 1, 0, 0,
            new DerivedStatModifiers { PhysicalDefense = double.PositiveInfinity }));
    }

    [Fact]
    public void RepeatedCalculationDoesNotAllocateAfterWarmup()
    {
        var calculator = Calculator;
        var actor = _catalog.Creatures["test_adventurer"];
        var weapon = _catalog.Weapons[actor.WeaponId];
        for (var i = 0; i < 1000; i++)
            calculator.WeaponPower(weapon, calculator.Calculate(actor));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            calculator.WeaponPower(weapon, calculator.Calculate(actor));
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void AssertFinite(DerivedStats stats)
    {
        foreach (var property in typeof(DerivedStats).GetProperties())
            Assert.True(double.IsFinite((double)property.GetValue(stats)!));
    }
}
