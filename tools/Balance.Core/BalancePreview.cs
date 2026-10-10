using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Development;
using Content.Server.Stats;
using Content.Server.Combat;

namespace ProjectG.Balance;

public sealed record BalancePreview(DerivedStats Stats, double Hit, double Interval, double ExpectedDps, double TargetHealth, double TargetDefense, double AvailablePoints)
{
    /// <summary>Expected stationary autoattack DPS. No rotation, movement, parry, or temporary passives are assumed.</summary>
    public static BalancePreview Calculate(ContentCatalog catalog, BalanceTestBuild build, double criticalMultiplier, int tickRate = 20)
    {
        var calculator = new StatCalculator(catalog.Balance);
        var stats = build.CalculateStats(catalog);
        var weapon = catalog.Weapons[catalog.Items[build.WeaponItemId].WeaponId!];
        var target = calculator.Calculate(catalog.Creatures[build.TargetId]);
        var power = calculator.WeaponPower(weapon, stats);
        power = CombatBalanceMath.BasicPower(power, catalog.Swordsman is { } sword && build.ProfessionId == sword.ProfessionId && weapon.IsSword ? sword.BasicDamageBonus : 0);
        var hit = calculator.ApplyDefense(power, target.PhysicalDefense);
        var interval = Math.Max(1d / tickRate, calculator.AttackInterval(weapon.AttackIntervalSeconds, stats));
        var expected = hit * (1 + StatCalculator.CriticalProbability(stats) * (criticalMultiplier - 1)) / interval;
        var baseStats = catalog.Creatures["test_adventurer"].Stats;
        double Sum(BaseStats s) => s.Strength + s.Agility + s.Vitality + s.Intelligence + s.Dexterity + s.Luck;
        var points = catalog.Progression.InitialStatPoints + (build.Level - 1) * catalog.Progression.StatPointsPerLevel - (Sum(build.Stats) - Sum(baseStats));
        return new(stats, hit, interval, expected, target.MaxHealth, target.PhysicalDefense, points);
    }

    public static double TechniqueHit(ContentCatalog catalog, BalanceTestBuild build, string abilityId)
    {
        var technique = catalog.Abilities[abilityId].Melee ?? throw new ArgumentException("Не ближний приём.");
        var calculator = new StatCalculator(catalog.Balance);
        var power = calculator.WeaponPower(catalog.Weapons[catalog.Items[build.WeaponItemId].WeaponId!], build.CalculateStats(catalog));
        var armor = calculator.Calculate(catalog.Creatures[build.TargetId]).PhysicalDefense;
        if (armor > 0) armor *= 1 - technique.ArmorIgnore;
        var curve = catalog.SkillProgressions[abilityId];
        return calculator.ApplyDefense(CombatBalanceMath.TechniquePower(power, technique, 1,
            levelFactor: curve.PowerFactor(Math.Min(build.SkillLevel, curve.LevelCap))), armor);
    }
}
