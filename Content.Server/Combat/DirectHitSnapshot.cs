namespace Content.Server.Combat;

/// <summary>Source offense captured at release; target defenses are deliberately absent.</summary>
internal readonly record struct DirectHitSnapshot(double Power,double Accuracy,double CriticalChance,double CriticalDamage,
    CombatSourceId TauntSource=default,double OtherTargetDamage=1);
