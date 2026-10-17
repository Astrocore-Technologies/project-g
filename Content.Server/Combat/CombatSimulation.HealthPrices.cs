using Content.Server.Data;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal bool CanPayHealth(NetworkEntityId id,double cost,bool allowFatal=false)
    {
        if(!double.IsFinite(cost) || cost<0) throw new ArgumentException("Invalid health price.");
        return _actors.TryGetValue(id,out var actor) && actor.Active && actor.Health>0 &&
            (allowFatal ? actor.Health>=cost : actor.Health-cost>0);
    }

    internal bool PayHealth(NetworkEntityId id,double cost,bool allowFatal=false)
    {
        if(!CanPayHealth(id,cost,allowFatal)) return false;
        var actor=_actors[id]; actor.Health=Math.Max(0,actor.Health-cost);
        ReportHealthPrice(actor,cost);
        if(actor.Health<=0) CompleteDeath(id,id);
        return true;
    }

    internal void PayPartialHealth(NetworkEntityId id,double cost,bool allowFatal)
    {
        if(!double.IsFinite(cost)||cost<0)throw new ArgumentException("Invalid partial health price.");
        if(!_actors.TryGetValue(id,out var actor)||actor.Health<=0)return;
        var maximum=allowFatal?actor.Health:Math.BitDecrement(actor.Health);
        PayHealth(id,Math.Min(cost,maximum),allowFatal);
    }

    // A lethal price is reserved within the activation transaction. The paid main
    // payload is captured/released before death cleanup can stop its ordinary action.
    internal void CommitFatalHealthPrice(NetworkEntityId id,double reserved)
    {
        if(!_actors.TryGetValue(id,out var actor)||actor.Health<=0)return;
        actor.Health=0;ReportHealthPrice(actor,reserved);CompleteDeath(id,id);
    }

    private void ReportHealthPrice(Combatant actor,double cost)
    {
        if(cost<=0)return;
        _effectDirty.Add(actor.Id);_equipmentDirty.Add(actor.Id);RefreshHealthRecovery(actor.Id);RefreshPassiveConditions(actor.Id);
        ReportEffectEvent(new(default,actor.SourceIdentity,actor.Id,CombatEffectEventKind.HealthPrice,cost));
    }

    internal double SelfDamage(NetworkEntityId id,double power,EffectDamageFamily family)
    {
        if(!double.IsFinite(power) || power<0 || !Enum.IsDefined(family)) throw new ArgumentException("Invalid self damage.");
        var actor=_actors[id]; var defense=family==EffectDamageFamily.Physical ? actor.Stats.PhysicalDefense : actor.Stats.MagicDefense;
        return ApplyHealthDamage(actor.SourceIdentity,id,id,_calculator.ApplyDefense(power,defense),DamageOrigin.SelfDamage,family);
    }
}
