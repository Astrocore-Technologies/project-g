namespace Content.Server.Data;

/// <summary>Server-only melee rules. Factors use an unmodified ordinary sword strike, before armor.</summary>
public sealed record MeleeTechnique
{
    public double DamageFactor { get; init; } = 1;
    public float RangeFactor { get; init; } = 1;
    public float ArcDegrees { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public bool FirstTargetOnly { get; init; }
    public float? OriginHeight {get;init;}
    public float? Thickness {get;init;}
    public bool NarrowThrust { get; init; }
    public bool Stationary { get; init; }
    public bool RequiresParry { get; init; }
    public double ExecuteThreshold { get; init; }
    public double ExecuteFactor { get; init; }
    public double RestoreStamina { get; init; }
    public double HitRecoverySeconds { get; init; }
    public double MissRecoverySeconds { get; init; }
    public double HitCancelAfterSeconds { get; init; }
    public string[] HitFollowups { get; init; } = [];
    public double MinimumWindupSeconds { get; init; }

    public void Validate()
    {
        double[] values = [DamageFactor, RangeFactor, ArcDegrees,
            ExecuteThreshold, ExecuteFactor, RestoreStamina,
            HitRecoverySeconds, MissRecoverySeconds, HitCancelAfterSeconds, MinimumWindupSeconds];
        if (OriginHeight is {} origin && (!float.IsFinite(origin) || origin is <0 or >128) ||
            Thickness is {} thickness && (!float.IsFinite(thickness) || thickness is <=0 or >128) ||
            values.Any(v => !double.IsFinite(v) || v < 0 || v > 360) || RangeFactor is <= 0 or > 3 ||
            ArcDegrees is < 0 or > 360 || ExecuteThreshold > 1 ||
            (ExecuteThreshold > 0) != (ExecuteFactor > 0) || HitRecoverySeconds > 2 || MissRecoverySeconds > 2 ||
            MinimumWindupSeconds > 2 || HitCancelAfterSeconds > HitRecoverySeconds || HitFollowups is null || HitFollowups.Length > 4 ||
            HitFollowups.Any(string.IsNullOrWhiteSpace) || HitFollowups.Distinct().Count() != HitFollowups.Length)
            throw new ArgumentException("Invalid melee technique balance.");
    }
}
