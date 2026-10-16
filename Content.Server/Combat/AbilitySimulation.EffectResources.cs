using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    internal void ResumeAfterRevival(NetworkEntityId id)
    {if(_actors.ContainsKey(id)){_dirty.Add(id);RefreshManaRecovery(id);}}
    internal void DisconnectInput(NetworkEntityId id)
    { _pending.Remove(id);_buffered.Remove(id);_cancelPending.Remove(id); }
    internal void FinishDeparture(NetworkEntityId id)=>StopActions(id,ActionStopReason.Departure);
    private double DrainEffectMana(NetworkEntityId id,double amount)
    {
        if(!_actors.TryGetValue(id,out var actor) || amount<=0) return 0;
        var removed=Math.Min(actor.Mana,amount); actor.Mana-=removed;
        if(removed>0) {_dirty.Add(id);RefreshManaRecovery(id);} return removed;
    }
    private double RestoreEffectMana(NetworkEntityId id,double amount)
    {
        if(!_actors.TryGetValue(id,out var actor) || amount<=0 || _combat.Get(id).Health<=0) return 0;
        var restored=Math.Min(Math.Max(0,actor.MaxMana-actor.Mana),amount); actor.Mana+=restored;
        if(restored>0) {_dirty.Add(id);RefreshManaRecovery(id);} return restored;
    }
}
