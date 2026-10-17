using Content.Server.Stats;
using Content.Shared.Combat;

namespace Content.Server.Data;

/// <summary>A weapon's share of its full attack interval, with independently authored phase policy.</summary>
public sealed record BasicAttackPhaseDefinition
{
    public required double Fraction { get; init; }
    public float MovementMultiplier { get; init; } = 1;
    public bool CanTurn { get; init; } = true;
    public ActionCancelPermission Cancel { get; init; }

    internal ActionPhaseDefinition At(double interval) => new()
    {
        Seconds=StatMath.Multiply(interval,Fraction),MovementMultiplier=MovementMultiplier,CanTurn=CanTurn,Cancel=Cancel
    };

    internal void Validate(string path)
    {
        if(!double.IsFinite(Fraction) || Fraction is <0 or >1)
            throw new ArgumentException($"{path}: fraction must be between zero and one.");
        At(1).Validate(path);
    }
}

/// <summary>Fractions sum to one complete weapon interval; one contact occurs on entering Active.</summary>
public sealed record BasicAttackDefinition
{
    public required BasicAttackPhaseDefinition Preparation { get; init; }
    public required BasicAttackPhaseDefinition Active { get; init; }
    public required BasicAttackPhaseDefinition Recovery { get; init; }
    public bool Interruptible { get; init; } = true;

    internal void Validate(string path)
    {
        if(Preparation is null || Active is null || Recovery is null)
            throw new ArgumentException($"{path}: all three basic phases are required.");
        Preparation.Validate(path+".preparation");Active.Validate(path+".active");Recovery.Validate(path+".recovery");
        if(Math.Abs(Preparation.Fraction+Active.Fraction+Recovery.Fraction-1)>1e-9)
            throw new ArgumentException($"{path}: basic phase fractions must sum to one weapon interval.");
    }

    internal ActionDefinition ForInterval(double interval,ActionCapability capabilities)
    {
        if(!double.IsFinite(interval) || interval<=0)throw new ArgumentException("A basic attack requires a positive finite interval.");
        return new()
        {
            Form=Preparation.Fraction>0 ? CombatActionForm.Cast : CombatActionForm.Instant,
            Category=CombatActionCategory.BasicAttack,Capabilities=capabilities,Interruptible=Interruptible,
            Preparation=Preparation.At(interval),Active=Active.At(interval),Recovery=Recovery.At(interval)
        };
    }
}
