namespace Content.Server.Stats;

/// <summary>Raw server attributes may be signed except MaxHealth. Execution helpers enforce physical bounds.</summary>
public readonly record struct DerivedStats(
    double MeleeAttack, double RangedAttack, double MagicAttack,
    double MeleeWeaponMultiplier, double RangedWeaponMultiplier,
    double PhysicalDefense, double MagicDefense,
    double MaxHealth, double MaxMana, double HealthRecovery, double ManaRecovery,
    double HealthItemMultiplier, double ManaItemMultiplier,
    double AttackSpeedMultiplier, double CastSpeedMultiplier, double CriticalChance);
