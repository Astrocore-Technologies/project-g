namespace Content.Shared.Combat;

/// <summary>Public context of an authoritative contact; bonuses remain server-authored rules.</summary>
[Flags]
public enum CombatHitTags : byte
{
    None=0, FrontHit=1, SideHit=2, BackHit=4, AirborneHit=8, ProneHit=16, ComboHit=32
}

public static class CombatHitTagRules
{
    public static bool IsValid(CombatHitTags value)
    {
        const CombatHitTags known=CombatHitTags.FrontHit|CombatHitTags.SideHit|CombatHitTags.BackHit|
            CombatHitTags.AirborneHit|CombatHitTags.ProneHit|CombatHitTags.ComboHit;
        var direction=(byte)(value&(CombatHitTags.FrontHit|CombatHitTags.SideHit|CombatHitTags.BackHit));
        return (value&~known)==0 && (direction==0 || (direction&(direction-1))==0) &&
            (value&(CombatHitTags.AirborneHit|CombatHitTags.ProneHit))!=(CombatHitTags.AirborneHit|CombatHitTags.ProneHit);
    }
}
