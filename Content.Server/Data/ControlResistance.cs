using Content.Shared.Combat;

namespace Content.Server.Data;

public sealed record ControlResistance
{
    public CrowdControlKind[] Immunities { get; init; } = [];
    public Dictionary<CrowdControlKind,double> Types { get; init; } = new();
    public Dictionary<ControlCategory,double> Categories { get; init; } = new();

    internal double Factor(CrowdControlKind kind)
    {
        if(Array.IndexOf(Immunities,kind)>=0) return 0;
        return (1-Types.GetValueOrDefault(kind))*(1-Categories.GetValueOrDefault(Content.Server.Combat.AdaptiveControlLedger.Category(kind)));
    }

    internal void Validate()
    {
        if(Immunities is null || Types is null || Categories is null || Immunities.Any(k=>!Enum.IsDefined(k)) ||
            Immunities.Distinct().Count()!=Immunities.Length || Types.Any(p=>!Enum.IsDefined(p.Key) || !double.IsFinite(p.Value) || p.Value is <0 or >1) ||
            Categories.Any(p=>!Enum.IsDefined(p.Key) || !double.IsFinite(p.Value) || p.Value is <0 or >1))
            throw new ArgumentException("Invalid control immunities or resistances.");
    }
}
