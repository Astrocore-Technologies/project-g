using System.Globalization;

namespace Content.Server.Progression;

/// <summary>Explicit experimental balance parameters; the exponent is the GDD 03 invariant.</summary>
public sealed record PowerExperienceCurveDefinition
{
    public required double Scale { get; init; }
    public required double Offset { get; init; }

    internal int Threshold(int level)
    {
        if (!double.IsFinite(Scale) || Scale <= 0 || !double.IsFinite(Offset) || Offset <= -1)
            throw new ArgumentException("Experience curve requires finite scale > 0 and offset > -1.");
        // Numeric convention for the experimental configuration; not UI rounding.
        var value = Math.Round(Scale * Math.Pow(level + Offset, 1.5), MidpointRounding.ToEven);
        if (!double.IsFinite(value) || value is < 1 or > 1000000000)
            throw new ArgumentException("Experience threshold exceeds the supported persistent/wire representation.");
        return checked((int)value);
    }
}

/// <summary>Prepared once at catalog load. Cap limits the table, never normalizes its thresholds.</summary>
public sealed class ExperienceCurve
{
    private readonly int[] _thresholds;
    public int LevelCap => _thresholds.Length;
    public string Key { get; }

    public ExperienceCurve(ProgressionBalance balance)
    {
        balance.Validate();
        _thresholds = new int[balance.LevelCap];
        for (var level = 1; level < balance.LevelCap; level++)
            _thresholds[level] = balance.LevelThreshold(level);
        Key = balance.ExperienceCurve is { } power
            ? string.Create(CultureInfo.InvariantCulture, $"power-1.5-even:{power.Scale:R}:{power.Offset:R}")
            : string.Create(CultureInfo.InvariantCulture, $"legacy-linear:{balance.ExperiencePerLevel}");
    }

    public int NextLevel(int level)
    {
        if (level < 1 || level > LevelCap) throw new ArgumentOutOfRangeException(nameof(level));
        return level == LevelCap ? 0 : _thresholds[level];
    }
}
