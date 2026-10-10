using Content.Server.Data;

namespace Content.Server.Combat;

/// <summary>Pure damage scaling shared by execution and local balancing tools, before defense and HP clipping.</summary>
public static class CombatBalanceMath
{
    public static double BasicPower(double weaponPower, double professionBonus, double focus = 1) => weaponPower * (focus * (1 + professionBonus));
    public static double TechniquePower(double weaponPower, MeleeTechnique technique, double health, double maximumHealth = 1, double levelFactor = 1, double focus = 1) =>
        weaponPower * (technique.ExecuteThreshold > 0 && health <= maximumHealth * technique.ExecuteThreshold ? technique.ExecuteFactor : technique.DamageFactor) * levelFactor * focus;
}
