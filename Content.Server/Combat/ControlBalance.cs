using Content.Shared.Combat;

namespace Content.Server.Combat;

/// <summary>GDD 01 approved first balance. Kept in server content, independently of transport and actor type.</summary>
public sealed record ControlBalance
{
    public double Lambda { get; init; } = 1;
    public double ResetSeconds { get; init; } = 12;
    public double HardBudget { get; init; } = 6;
    public double MovementBudget { get; init; } = 5;
    public double ActionBudget { get; init; } = 6;
    public double SoftBudget { get; init; } = 3;
    public double TargetingBudget { get; init; } = 2;
    public double MinimumSlowMultiplier { get; init; } = .1;
    public double BlindAccuracyUnits { get; init; } = 100;
    public double DefaultTauntPenalty { get; init; } = .3;

    internal double Budget(ControlCategory category)=>category switch
    {
        ControlCategory.Hard=>HardBudget,ControlCategory.Movement=>MovementBudget,ControlCategory.Action=>ActionBudget,
        ControlCategory.Soft=>SoftBudget,ControlCategory.Targeting=>TargetingBudget,
        _=>throw new ArgumentOutOfRangeException(nameof(category))
    };

    internal void Validate()
    {
        double[] positive=[ResetSeconds,HardBudget,MovementBudget,ActionBudget,SoftBudget,TargetingBudget,BlindAccuracyUnits];
        if(positive.Any(v=>!double.IsFinite(v) || v<=0) || !double.IsFinite(Lambda) || Lambda<0 ||
            !double.IsFinite(MinimumSlowMultiplier) || MinimumSlowMultiplier is <=0 or >1 ||
            !double.IsFinite(DefaultTauntPenalty) || DefaultTauntPenalty is <0 or >1)
            throw new ArgumentException("Invalid ACR/control balance.");
    }
}
