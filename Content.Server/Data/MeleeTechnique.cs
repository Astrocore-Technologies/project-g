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

    public void Validate()
    {
        double[] values = [DamageFactor, RangeFactor, ArcDegrees, ArmorIgnore, BleedFactor, BleedSeconds,
            StunSeconds, SlowFraction, SlowSeconds, ExecuteThreshold, ExecuteFactor, RestoreStamina];
        if (values.Any(v => !double.IsFinite(v) || v < 0 || v > 360) || RangeFactor is <= 0 or > 3 ||
            ArcDegrees is <= 0 or > 360 || ArmorIgnore > 1 || SlowFraction >= 1 || ExecuteThreshold > 1 ||
            (BleedFactor > 0) != (BleedSeconds > 0) || (SlowFraction > 0) != (SlowSeconds > 0) ||
            (ExecuteThreshold > 0) != (ExecuteFactor > 0))
            throw new ArgumentException("Invalid melee technique balance.");
    }
}
