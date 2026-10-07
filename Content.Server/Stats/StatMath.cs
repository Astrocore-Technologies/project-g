namespace Content.Server.Stats;

/// <summary>Finite arithmetic for validated finite inputs; saturation is a numeric guard, not a stat cap.</summary>
public static class StatMath
{
    public static double Add(double left, double right) => Finite(left + right);
    public static double Multiply(double left, double right) => Finite(left * right);

    // Linear growth above zero, reciprocal decline below zero. Continuous slope at zero,
    // no negative HP, inverted weapon damage or singularity at a negative stat value.
    public static double PositiveScale(double contribution) => contribution >= 0
        ? Add(1, contribution)
        : 1 / Add(1, -contribution);

    public static double ScalePositive(double value, double contribution) =>
        Math.Max(double.Epsilon, Multiply(value, PositiveScale(contribution)));

    public static double DividePositive(double value, double divisor) =>
        Math.Max(double.Epsilon, Finite(value / divisor));

    private static double Finite(double value) => double.IsPositiveInfinity(value)
        ? double.MaxValue
        : double.IsNegativeInfinity(value) ? -double.MaxValue : value;
}
