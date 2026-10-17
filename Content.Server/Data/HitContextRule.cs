using Content.Shared.Combat;

namespace Content.Server.Data;

/// <summary>One authored condition and factor. Tags never create automatic global bonuses.</summary>
public sealed record HitContextRule
{
    public required CombatHitTags RequiredTags {get;init;}
    public required CombatHitTags ForbiddenTags {get;init;}
    public double? TargetHealthBelowFraction {get;init;}
    public double? TargetHealthAboveFraction {get;init;}
    public required double DamageMultiplier {get;init;}
    public required bool CountsAsCombo {get;init;}
    internal bool Matches(CombatHitTags tags,double health,double maximum)=>
        (tags&RequiredTags)==RequiredTags && (tags&ForbiddenTags)==0 &&
        (TargetHealthBelowFraction is not {} below || health<maximum*below) &&
        (TargetHealthAboveFraction is not {} above || health>maximum*above);
    internal void Validate(string path)
    {
        if(!CombatHitTagRules.IsValid(RequiredTags)||!KnownTags(ForbiddenTags)||(RequiredTags&ForbiddenTags)!=0 ||
            RequiredTags==0&&ForbiddenTags==0&&TargetHealthBelowFraction is null&&TargetHealthAboveFraction is null ||
            TargetHealthBelowFraction is {} below&&(!double.IsFinite(below)||below is <=0 or >1) ||
            TargetHealthAboveFraction is {} above&&(!double.IsFinite(above)||above is <0 or >=1) ||
            TargetHealthBelowFraction is {} high&&TargetHealthAboveFraction is {} low&&low>=high ||
            !double.IsFinite(DamageMultiplier)||DamageMultiplier<0)
            throw new ArgumentException($"{path}: invalid hit context condition or multiplier.");
    }
    internal static bool KnownTags(CombatHitTags tags)=>(tags&~(CombatHitTags.FrontHit|CombatHitTags.SideHit|CombatHitTags.BackHit|
        CombatHitTags.AirborneHit|CombatHitTags.ProneHit|CombatHitTags.ComboHit))==0;
}
