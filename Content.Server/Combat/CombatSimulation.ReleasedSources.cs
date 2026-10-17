using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal Func<NetworkEntityId,ReleasedPvpSource?>? CaptureReleasedPvp {get;set;}
    internal Func<ReleasedSourceSnapshot,NetworkEntityId,bool>? ReleasedDamagePermission {get;set;}
    internal Action<ReleasedSourceSnapshot,NetworkEntityId,double>? ReleasedDamageApplied {get;set;}
    internal Action<CombatSourceId,CombatSourceId?,NetworkEntityId,double>? DetachedDamageApplied {get;set;}

    internal ReleasedSourceSnapshot CaptureReleasedSource(NetworkEntityId id,double skillPower)
    {
        var actor=_actors[id];
        return new(id,actor.SourceIdentity,actor.Kind,actor.Position,actor.Stats,
            _calculator.WeaponPower(actor.Weapon,actor.Stats),actor.PeriodicCriticalsAllowed || actor.PassiveAllowsPeriodicCriticals,skillPower,CaptureReleasedPvp?.Invoke(id),
            ContributionIdentity(actor.SourceIdentity),PlayerFamily(actor));
    }

    internal bool CanReleasedTarget(ReleasedSourceSnapshot source,NetworkEntityId target)
    {
        if(!_actors.TryGetValue(target,out var victim) || victim.Health<=0 || !victim.Active ||
            target==source.Actor || victim.SourceIdentity==source.Identity)return false;
        if(source.PlayerFamily is {} family && PlayerFamily(victim)==family)return false;
        if(ReleasedDamagePermission is {} permission)return permission(source,target);
        if(DamagePermission is {} regular)return regular(source.Actor,target);
        return IsNpc(source.Kind)?IsPlayerUnit(victim):IsHostileTarget(victim.Kind);
    }

    private bool SourceUnavailable(NetworkEntityId source)=>!_actors.TryGetValue(source,out var actor) || !actor.Active || actor.Health<=0;
}
