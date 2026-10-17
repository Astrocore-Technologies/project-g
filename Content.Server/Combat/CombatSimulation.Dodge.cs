using Content.Server.Data;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly record struct DodgeProtection(ActionInstance Action,double Until,DodgeThreats Threats);
    private readonly Dictionary<NetworkEntityId,DodgeProtection> _dodges=new();
    private readonly List<NetworkEntityId> _finishedDodges=new();

    internal void StartDodgeProtection(NetworkEntityId id,ActionInstance action,DodgeDefinition definition)
    {
        if(definition.ProtectionSeconds<=0)return;
        _dodges[id]=new(action,_time+definition.ProtectionSeconds,definition.ProtectedThreats);
    }
    private bool DodgeProtects(NetworkEntityId id,DamageOrigin origin,EffectDamageFamily family)
    {
        if(!_dodges.TryGetValue(id,out var dodge) || _time>=dodge.Until || dodge.Action.Finished ||
            !_actors.TryGetValue(id,out var actor) || actor.Health<=0 || !actor.Active)return false;
        var threat=origin switch
        {
            DamageOrigin.Direct=>family==EffectDamageFamily.Physical?DodgeThreats.DirectPhysical:DodgeThreats.DirectMagical,
            DamageOrigin.Periodic=>family==EffectDamageFamily.Physical?DodgeThreats.PeriodicPhysical:DodgeThreats.PeriodicMagical,
            _=>DodgeThreats.None
        };
        return threat!=DodgeThreats.None && (dodge.Threats&threat)!=0;
    }
    private void AdvanceDodges()
    {
        _finishedDodges.Clear();
        foreach(var (id,dodge) in _dodges)if(_time>=dodge.Until || dodge.Action.Finished)_finishedDodges.Add(id);
        foreach(var id in _finishedDodges)_dodges.Remove(id);
    }
}
