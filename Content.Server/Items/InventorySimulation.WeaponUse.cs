using Content.Shared.Network;

namespace Content.Server.Items;

public sealed partial class InventorySimulation
{
    private sealed class WeaponUse(Item item)
    {
        internal readonly Item Item=item;
        internal bool Worn;
    }
    private readonly HashSet<NetworkEntityId> _wearPending=new();
    private readonly List<NetworkEntityId> _wearCandidates=new();

    private void BeginWeaponUse(NetworkEntityId id,ulong execution)
    {
        if(execution==0 || !_actors.TryGetValue(id,out var actor))return;
        foreach(var item in actor.Items)
            if(item.Saved.EquippedSlot==EquipmentSlot.Weapon && item.Saved.Condition is {Current:>0})
            {
                if(!actor.WeaponUses.TryAdd(execution,new(item)))throw new InvalidOperationException("Duplicate weapon execution receipt.");
                return;
            }
    }
    private void EndWeaponUse(NetworkEntityId id,ulong execution)
    {if(_actors.TryGetValue(id,out var actor))actor.WeaponUses.Remove(execution);}

    private void QueueWear(NetworkEntityId id,ulong execution)
    {
        if(!_actors.TryGetValue(id,out var actor) || !actor.WeaponUses.TryGetValue(execution,out var receipt) || receipt.Worn)return;
        receipt.Worn=true;var item=receipt.Item;
        if(item.Saved.Condition is not {Current:>0} state || item.Definition.Condition is not {} rule)return;
        if(state.Revision==long.MaxValue)throw new InvalidOperationException("Item revision exhausted.");
        // Only the private deferred debit changes. Current, its public revision and bonuses remain valid for this action.
        var pending=Math.Min(state.Current,state.PendingWear+rule.WearPerHit);
        if(pending==state.PendingWear)return;
        item.Saved=item.Saved with {Condition=state with {PendingWear=pending}};_wearPending.Add(id);_dirty.Add(id);
    }

    internal void FlushWear(NetworkEntityId id)
    {
        if(!_wearPending.Contains(id) || !_actors.TryGetValue(id,out var actor) ||
            combat.TryGet(id,out var combatant) && combatant.Action is {Finished:false})return;
        var changed=false;var broken=false;
        foreach(var item in actor.Items)
            if(item.Saved.Condition is {PendingWear:>0} state)
            {
                if(state.Revision==long.MaxValue)throw new InvalidOperationException("Item revision exhausted.");
                item.Saved=item.Saved with {Condition=state with {Current=state.Current-state.PendingWear,PendingWear=0,Revision=state.Revision+1}};
                changed=true;broken|=item.Saved.Condition.Current==0;
            }
        _wearPending.Remove(id);
        if(changed)
        {
            if(broken){RefreshStats(id);combat.RefreshBlockPermission(id);}
            _dirty.Add(id);
        }
    }

    internal void FlushFinishedWear()
    {
        _wearCandidates.Clear();_wearCandidates.AddRange(_wearPending);
        foreach(var id in _wearCandidates)FlushWear(id);
    }
}
