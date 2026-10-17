namespace Content.Server.Data;

public enum SkillEffectTrigger : byte { Activated, Contact, HealthDamage, UnblockedHealthDamage }
public enum SkillEffectTarget : byte { Self, HitTarget }
public enum SkillEffectOperation : byte { Timed, Heal, Resurrect, Control, Cleanse, Dispel, Threat, CooldownReset, SelfDamage }

public sealed record SkillEffectApplication
{
    public SkillEffectOperation Operation {get;init;}
    public string? EffectId { get; init; }
    public EffectPower? Power {get;init;}
    public SkillControlDefinition? Control {get;init;}
    public SkillRemovalDefinition? Removal {get;init;}
    public string? ResetSkillId {get;init;}
    public EffectDamageFamily? SelfDamageFamily {get;init;}
    public required SkillEffectTrigger Trigger { get; init; }
    public required SkillEffectTarget Target { get; init; }
    public bool RequiresBleedable { get; init; }
    public bool ScalePowerWithSkillLevel { get; init; }
    public bool AllowOtherPlayers {get;init;}
    public Content.Shared.Combat.CombatHitTags RequiredHitTags {get;init;}
    public Content.Shared.Combat.CombatHitTags ForbiddenHitTags {get;init;}
}
