using Content.Shared.Combat;

namespace Content.Server.Data;

/// <summary>Authored action/protection timing; movement may reach a nearby wall before this action ends.</summary>
public record DodgeDefinition
{
    public required double DurationSeconds {get;init;}
    public required double ProtectionSeconds {get;init;}
    public required DodgeThreats ProtectedThreats {get;init;}
    internal virtual void Validate()
    {
        if(!double.IsFinite(DurationSeconds) || DurationSeconds is <=0 or >60 ||
            !double.IsFinite(ProtectionSeconds) || ProtectionSeconds<0 || ProtectionSeconds>DurationSeconds ||
            (ProtectedThreats&~DodgeThreats.All)!=0 || (ProtectionSeconds>0)!=(ProtectedThreats!=DodgeThreats.None))
            throw new ArgumentException("Invalid authored dodge duration or protection.");
    }
}

/// <summary>A profession override, separate from the ordinary ability's range, price and cooldown.</summary>
public sealed record DodgeProfileDefinition : DodgeDefinition
{
    public required float Range {get;init;}
    public required double StaminaCost {get;init;}
    public required double CooldownSeconds {get;init;}
    public float Speed=>(float)(Range/DurationSeconds);
    internal override void Validate()
    {
        base.Validate();
        if(!float.IsFinite(Range) || Range<=0 || !float.IsFinite(Speed) || Speed<=0 ||
            !double.IsFinite(StaminaCost) || StaminaCost<0 ||
            !double.IsFinite(CooldownSeconds) || CooldownSeconds is <0 or >86400)
            throw new ArgumentException("Invalid authored dodge range, price or cooldown.");
    }
}
