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
    public double InputBufferSeconds { get; init; } = .2;
    public double ControlResetSeconds { get; init; } = 4;
    public double RepeatedControlFactor { get; init; } = .5;
    public double QuickRecoverCost { get; init; } = 15;
    public double QuickRecoverCooldown { get; init; } = 12;
    public float QuickRecoverRange { get; init; } = 1.5f;
    public float QuickRecoverSpeed { get; init; } = 6;
    public void Validate()
    {
        double[] values = [MaxStamina,RecoveryPerSecond,RecoveryDelay,BlockCost,ParryCost,DodgeCost,ParryWindow,ParryCooldown,GuardLeaseSeconds,
            InputBufferSeconds,ControlResetSeconds,RepeatedControlFactor,QuickRecoverCost,QuickRecoverCooldown,QuickRecoverRange,QuickRecoverSpeed];
        if (values.Any(v=>!double.IsFinite(v) || v<=0 || v>100000) ||
            BlockCost>MaxStamina || ParryCost>MaxStamina || DodgeCost>MaxStamina ||
            ParryWindow>ParryCooldown || ParryCooldown>60 || GuardLeaseSeconds>2 ||
            !float.IsFinite(BlockMovementMultiplier) || BlockMovementMultiplier is <=0 or >1 ||
            InputBufferSeconds > .3 || ControlResetSeconds > 10 || RepeatedControlFactor > 1 ||
            QuickRecoverCost > MaxStamina || QuickRecoverCooldown > 60 || QuickRecoverRange > 3 || QuickRecoverSpeed > 20 ||
            QuickRecoverRange / QuickRecoverSpeed > 2)
            throw new ArgumentException("Invalid defense balance.");
    }
}
