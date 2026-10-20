using Content.Server.Stats;

namespace Content.Server.Combat;

/// <summary>Server resource arithmetic. A capacity change is not a healing/damage event.</summary>
internal static class ResourceTransition
{
    internal static double Amount(double current, double previousMaximum, double maximum, bool preserveFraction = true)
    {
        maximum = Math.Max(0, maximum);
        if (maximum == 0 || current <= 0) return 0;
        if (previousMaximum == maximum) return Math.Min(current,maximum);
        if (!preserveFraction) return Math.Min(current, maximum);
        if (previousMaximum <= 0) return 0;
        // Divide first: multiplying two large finite resource values could overflow.
        return Math.Clamp(current / previousMaximum, 0, 1) * maximum;
    }

    internal static bool CanFlow(double current, double maximum, double rate) =>
        rate < 0 ? current > 0 : rate > 0 && current < Math.Max(0, maximum);

    internal static double SecondsToLimit(double current, double maximum, double rate) =>
        rate < 0 ? current / -rate : rate > 0 ? Math.Max(0, maximum - current) / rate : double.PositiveInfinity;

    internal static double Flow(double current, double maximum, double rate, double elapsed, double clockTolerance)
    {
        if (elapsed <= 0 || !CanFlow(current, maximum, rate)) return current;
        // Snap only the mathematical endpoint, within clock representation error.
        if (SecondsToLimit(current, maximum, rate) <= elapsed + clockTolerance)
            return rate < 0 ? 0 : Math.Max(0, maximum);
        return Math.Clamp(StatMath.Add(current, StatMath.Multiply(rate, elapsed)), 0, Math.Max(0, maximum));
    }
}
