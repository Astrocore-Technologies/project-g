namespace Content.Server.Data;

/// <summary>Per-creature threat weights. Participation credit is independent of these weights.</summary>
public sealed record ThreatDefinition
{
    public required double DamagePerHp {get;init;}
    public required double HealingPerHp {get;init;}
    internal void Validate(string path)
    {
        if(!double.IsFinite(DamagePerHp) || DamagePerHp<0 || !double.IsFinite(HealingPerHp) || HealingPerHp<0)
            throw new ArgumentException($"{path}: threat weights must be finite and nonnegative.");
    }
}
