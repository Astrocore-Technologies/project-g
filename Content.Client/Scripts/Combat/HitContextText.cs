using Content.Shared.Combat;

namespace ProjectG.Combat;

internal static class HitContextText
{
    internal static string Suffix(CombatHitTags tags)
    {
        if(tags==CombatHitTags.None)return "";
        var text=(tags&(CombatHitTags.FrontHit|CombatHitTags.SideHit|CombatHitTags.BackHit)) switch
        {
            CombatHitTags.FrontHit=>" · спереди",CombatHitTags.SideHit=>" · сбоку",CombatHitTags.BackHit=>" · со спины",_=>""
        };
        if((tags&CombatHitTags.AirborneHit)!=0)text+=" · в воздухе";
        if((tags&CombatHitTags.ProneHit)!=0)text+=" · по лежащей цели";
        if((tags&CombatHitTags.ComboHit)!=0)text+=" · комбо";
        return text;
    }
}
