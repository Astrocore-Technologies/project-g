using Content.Shared.Network;
using Content.Server.Data;
using Content.Server.Stats;
namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal double ResolveHealing(NetworkEntityId source,NetworkEntityId target,EffectPower power,double scale,ulong execution,bool allowOtherPlayers=false)
    {
        if(!CanSupport(source,target,allowOtherPlayers) || _actors[target].Health<=0)return 0;
        var stats=_actors[source].Stats;
        var critical=StatCalculator.CriticalProbability(stats)>0 && _roll()<StatCalculator.CriticalProbability(stats);
        var amount=StatMath.Multiply(EffectPowerAtSource(source,power),scale);
        if(critical)amount=StatMath.Multiply(amount,Math.Max(0,stats.CriticalDamage));
        var restored=Heal(source,target,amount,allowOtherPlayers:allowOtherPlayers,reportJournal:false);
        if(restored>0)ReportEffectEvent(new(Guid.Empty,SourceIdentity(source),target,CombatEffectEventKind.Healing,restored,critical,execution));
        return restored;
    }

    internal bool ResolveResurrection(NetworkEntityId source,NetworkEntityId target,EffectPower power,double scale,ulong execution,bool allowOtherPlayers=false)
    {
        if(!CanSupport(source,target,allowOtherPlayers) || !_actors.TryGetValue(target,out var actor) ||
            !CanResurrect(target,actor.DeathId))return false;
        var amount=StatMath.Multiply(EffectPowerAtSource(source,power),scale);
        if(amount<=0 || !Resurrect(source,target,actor.DeathId,amount,allowOtherPlayers))return false;
        ReportEffectEvent(new(Guid.Empty,SourceIdentity(source),target,CombatEffectEventKind.Resurrection,actor.Health,false,execution));
        return true;
    }

    internal bool CanSupport(NetworkEntityId source,NetworkEntityId target,bool allowOtherPlayers=false)
    {
        if(!_actors.TryGetValue(source,out var author) || !author.Active || author.Health<=0 ||
            !_actors.TryGetValue(target,out var recipient) || !recipient.Active)return false;
        return source==target || SupportPermission?.Invoke(source,target)==true ||
            allowOtherPlayers && author.Kind==CombatEntityKind.Player && recipient.Kind==CombatEntityKind.Player;
    }
}
