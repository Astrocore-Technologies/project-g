namespace Content.Server.Stats;

/// <summary>Sparse data-driven coefficients; omitted weights mean no contribution.</summary>
public sealed record StatWeights
{
    public double Strength { get; init; }
    public double Agility { get; init; }
    public double Vitality { get; init; }
    public double Intelligence { get; init; }
    public double Dexterity { get; init; }
    public double Luck { get; init; }

    public bool IsValid() => Valid(Strength) && Valid(Agility) && Valid(Vitality) &&
        Valid(Intelligence) && Valid(Dexterity) && Valid(Luck);

    public double Evaluate(BaseStats stats)
    {
        var value = StatMath.Multiply(Strength, stats.Strength);
        value = StatMath.Add(value, StatMath.Multiply(Agility, stats.Agility));
        value = StatMath.Add(value, StatMath.Multiply(Vitality, stats.Vitality));
        value = StatMath.Add(value, StatMath.Multiply(Intelligence, stats.Intelligence));
        value = StatMath.Add(value, StatMath.Multiply(Dexterity, stats.Dexterity));
        return StatMath.Add(value, StatMath.Multiply(Luck, stats.Luck));
    }

    private static bool Valid(double value) => double.IsFinite(value) && value >= 0;
}
