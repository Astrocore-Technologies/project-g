namespace Content.Server.Data;

/// <summary>Eligibility belongs to the action, independently of targeting and geometry.</summary>
public sealed record HitDefense
{
    public bool Blockable { get; init; }
    public bool Parryable { get; init; }
    public double PressureScale { get; init; } = 1;
    internal static HitDefense Melee { get; } = new() { Blockable=true,Parryable=true };
    internal static HitDefense Projectile { get; } = new() { Blockable=true };
    internal static HitDefense Unblockable { get; } = new();
    internal void Validate()
    {
        if (!double.IsFinite(PressureScale) || PressureScale < 0)
            throw new ArgumentException("Block pressure scale must be finite and non-negative.");
    }
}
