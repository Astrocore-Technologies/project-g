namespace Content.Server.Data;

/// <summary>Server-only melee rules. Factors use an unmodified ordinary sword strike, before armor.</summary>
public sealed record MeleeTechnique
{
    public double DamageFactor { get; init; } = 1;
    public float RangeFactor { get; init; } = 1;
    public float ArcDegrees { get; init; } = 70;
    public bool FirstTargetOnly { get; init; } = true;
    public bool NarrowThrust { get; init; }
    public bool Stationary { get; init; }
    public bool RequiresParry { get; init; }
    public double ArmorIgnore { get; init; }
    public double BleedFactor { get; init; }
    public double BleedSeconds { get; init; }
    public double StunSeconds { get; init; }
    public float SlowFraction { get; init; }
    public double SlowSeconds { get; init; }
    public double ExecuteThreshold { get; init; }
    public double ExecuteFactor { get; init; }
    public double RestoreStamina { get; init; }
    public double HitRecoverySeconds { get; init; }
    public double MissRecoverySeconds { get; init; }
    public double HitCancelAfterSeconds { get; init; }
    public string[] HitFollowups { get; init; } = [];
    public bool BlockPreventsBleed { get; init; }
    public double KnockupSeconds { get; init; }
    public double KnockdownSeconds { get; init; }
    public float KnockupHeight { get; init; }
    public float KnockbackDistance { get; init; }
    public float KnockbackSpeed { get; init; }
    public double MinimumWindupSeconds { get; init; }

    public void Validate()
    {
        double[] values = [DamageFactor, RangeFactor, ArcDegrees, ArmorIgnore, BleedFactor, BleedSeconds,
            StunSeconds, SlowFraction, SlowSeconds, ExecuteThreshold, ExecuteFactor, RestoreStamina,
            HitRecoverySeconds, MissRecoverySeconds, HitCancelAfterSeconds, KnockupSeconds, KnockdownSeconds,
            KnockupHeight, KnockbackDistance, KnockbackSpeed, MinimumWindupSeconds];
        if (values.Any(v => !double.IsFinite(v) || v < 0 || v > 360) || RangeFactor is <= 0 or > 3 ||
            ArcDegrees is <= 0 or > 360 || ArmorIgnore > 1 || SlowFraction >= 1 || ExecuteThreshold > 1 ||
            (BleedFactor > 0) != (BleedSeconds > 0) || (SlowFraction > 0) != (SlowSeconds > 0) ||
            (ExecuteThreshold > 0) != (ExecuteFactor > 0) || HitRecoverySeconds > 2 || MissRecoverySeconds > 2 ||
            MinimumWindupSeconds > 2 || HitCancelAfterSeconds > HitRecoverySeconds || HitFollowups is null || HitFollowups.Length > 4 ||
            HitFollowups.Any(string.IsNullOrWhiteSpace) || HitFollowups.Distinct().Count() != HitFollowups.Length ||
            StunSeconds > 2 || (KnockupSeconds > 0) != (KnockupHeight > 0) || KnockupSeconds > 2 || KnockdownSeconds > 2 ||
            KnockupHeight > 3 || KnockbackDistance > 3 ||
            (KnockbackDistance > 0) != (KnockbackSpeed > 0) || KnockbackSpeed > 20 ||
            KnockbackDistance > 0 && KnockbackDistance / KnockbackSpeed > 2)
            throw new ArgumentException("Invalid melee technique balance.");
    }
}
