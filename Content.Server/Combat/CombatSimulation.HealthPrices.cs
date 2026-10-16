using Content.Server.Data;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal bool CanPayHealth(NetworkEntityId id,double cost,bool allowFatal=false)
    {
        if(!double.IsFinite(cost) || cost<0) throw new ArgumentException("Invalid health price.");
        return _actors.TryGetValue(id,out var actor) && actor.Active && actor.Health>0 &&
            (allowFatal ? actor.Health>=cost : actor.Health>cost);
    }

    internal bool PayHealth(NetworkEntityId id,double cost,bool allowFatal=false)
    {
        if(!CanPayHealth(id,cost,allowFatal)) return false;
        var actor=_actors[id]; actor.Health=Math.Max(0,actor.Health-cost);
        if(cost>0) {_effectDirty.Add(id);_equipmentDirty.Add(id);RefreshHealthRecovery(id);}
        ReportEffectEvent(new(default,actor.SourceIdentity,id,CombatEffectEventKind.HealthPrice,cost));
        if(actor.Health<=0) CompleteDeath(id,id);
        return true;
    }

    internal double SelfDamage(NetworkEntityId id,double power,EffectDamageFamily family)
    {
        if(!double.IsFinite(power) || power<0 || !Enum.IsDefined(family)) throw new ArgumentException("Invalid self damage.");
        var actor=_actors[id]; var defense=family==EffectDamageFamily.Physical ? actor.Stats.PhysicalDefense : actor.Stats.MagicDefense;
        return ApplyHealthDamage(actor.SourceIdentity,id,id,_calculator.ApplyDefense(power,defense),DamageOrigin.SelfDamage,family);
    }
}
