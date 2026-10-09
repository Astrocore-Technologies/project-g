namespace Content.Server.Combat;

/// <summary>Temporary, data-driven defense resource tuning.</summary>
public sealed record DefenseBalance
{
    public double MaxStamina { get; init; } = 100;
    public double RecoveryPerSecond { get; init; } = 15;
    public double RecoveryDelay { get; init; } = 1;
    public double BlockCost { get; init; } = 10;
    public double ParryCost { get; init; } = 10;
    public double DodgeCost { get; init; } = 20;
    public double ParryWindow { get; init; } = .25;
    public double ParryCooldown { get; init; } = .5;
    public float BlockMovementMultiplier { get; init; } = .5f;
    public double GuardLeaseSeconds { get; init; } = .5;
    public void Validate()
    {
        double[] values = [MaxStamina,RecoveryPerSecond,RecoveryDelay,BlockCost,ParryCost,DodgeCost,ParryWindow,ParryCooldown,GuardLeaseSeconds];
        if (values.Any(v=>!double.IsFinite(v) || v<=0 || v>100000) ||
            BlockCost>MaxStamina || ParryCost>MaxStamina || DodgeCost>MaxStamina ||
            ParryWindow>ParryCooldown || ParryCooldown>60 || GuardLeaseSeconds>2 ||
            !float.IsFinite(BlockMovementMultiplier) || BlockMovementMultiplier is <=0 or >1)
            throw new ArgumentException("Invalid defense balance.");
    }
}
