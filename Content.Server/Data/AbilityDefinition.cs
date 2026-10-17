namespace Content.Server.Data;

public enum AbilityContactDisposition : byte { Harmful, Support }

/// <summary>Server-only skill definition; a public profile reveals only an owned ability's execution parameters.</summary>
public sealed record AbilityDefinition
{
    public required string Id { get; init; }
    public required ushort NetworkId { get; init; }
    public required AbilityKind Kind { get; init; }
    public required double Power { get; init; }
    public required double ManaCost { get; init; }
    public required double CooldownSeconds { get; init; }
    public byte MaxCharges { get; init; } = 1;
    public bool ManualOnly {get;init;}
    public required ActionDefinition Action { get; init; }
    public ChannelDefinition? Channel {get;init;}
    public ChargeDefinition? Charge {get;init;}
    public ToggleDefinition? Toggle {get;init;}
    public PassiveDefinition? Passive {get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    public double CastSeconds => Action.Preparation.Seconds>0 ? Action.Preparation.Seconds : Action.Active.Seconds;
    public required double Range { get; init; }
    public required double Radius { get; init; }
    public required double Speed { get; init; }
    public float? ProjectileLaunchHeight { get; init; }
    public GroundAreaDefinition? GroundVolume {get;init;}
    public DodgeDefinition? Dodge {get;init;}
    public required double MagicAttackScale { get; init; }
    public double StaminaCost { get; init; }
    public double HealthCost {get;init;}
    public bool AllowFatalHealthCost {get;init;}
    public MeleeTechnique? Melee { get; init; }
    public AbilityContactDisposition ContactDisposition {get;init;}
    public SkillEffectApplication[] Effects { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Interruptible => Action.Interruptible;
    public HitDefense? Defense { get; init; }
    public HitContextRule? HitContext {get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    public HitDefense HitRules => Defense ?? (Kind == AbilityKind.Melee ? HitDefense.Melee :
        Kind == AbilityKind.Projectile ? HitDefense.Projectile : HitDefense.Unblockable);
    public Content.Server.Progression.SkillProgressionDefinition? Progression { get; init; }

    internal DodgeProfileDefinition AuthoredDodgeProfile()=>new()
    {Range=(float)Range,StaminaCost=StaminaCost,CooldownSeconds=CooldownSeconds,DurationSeconds=Dodge!.DurationSeconds,
        ProtectionSeconds=Dodge.ProtectionSeconds,ProtectedThreats=Dodge.ProtectedThreats};

    internal AbilityDefinition WithDodgeProfile(DodgeProfileDefinition? profession)
    {
        if(Kind!=AbilityKind.Dash)return this;
        var profile=profession??AuthoredDodgeProfile();
        return this with {Range=profile.Range,Speed=profile.Speed,StaminaCost=profile.StaminaCost,
            CooldownSeconds=profile.CooldownSeconds,Dodge=profile};
    }
}
