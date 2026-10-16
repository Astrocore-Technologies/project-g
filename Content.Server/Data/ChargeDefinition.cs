using Content.Server.Stats;

namespace Content.Server.Data;

public enum ChargeFullBehavior : byte { Release, Hold }

/// <summary>Authored charge timing and linear power curve; no profession balance is implied.</summary>
public sealed record ChargeDefinition
{
    public required double FullSeconds {get;init;}
    public required ActionSpeedSource Speed {get;init;}
    public required ChargeFullBehavior AtFull {get;init;}
    public required double StartPower {get;init;}
    public required double FullPower {get;init;}

    internal void Validate(string path)
    {
        if(!double.IsFinite(FullSeconds) || FullSeconds<=0 || !float.IsFinite((float)FullSeconds) || (float)FullSeconds<=0 ||
            !Enum.IsDefined(Speed) || !Enum.IsDefined(AtFull) || !double.IsFinite(StartPower) || StartPower<0 ||
            !double.IsFinite(FullPower) || FullPower<0)
            throw new ArgumentException($"{path}: explicit finite timing, full-charge behavior and power endpoints are required.");
    }
    internal double Duration(StatCalculator calculator,DerivedStats stats)=>Speed switch
    {
        ActionSpeedSource.AttackSpeed=>calculator.AttackInterval(FullSeconds,stats),
        ActionSpeedSource.CastSpeed=>calculator.CastDuration(FullSeconds,stats),
        _=>FullSeconds
    };
    internal double Power(double fraction)=>StartPower*(1-Math.Clamp(fraction,0,1))+FullPower*Math.Clamp(fraction,0,1);
}
