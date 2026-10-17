using Content.Shared.Network;

namespace Content.Server.Items;

public sealed partial class InventorySimulation
{
    private sealed class WeaponUse(Guid item,int wear)
    {
        internal readonly Guid Item=item;
        internal readonly int Wear=wear;
        internal bool Worn;
    }
    private readonly Dictionary<(NetworkEntityId Actor,ulong Execution),WeaponUse> _weaponUses=new();
    private readonly HashSet<NetworkEntityId> _wearPending=new();
    private readonly List<NetworkEntityId> _wearCandidates=new();

    private void BeginWeaponUse(NetworkEntityId id,ulong execution)
    {
        if(execution==0 || !_actors.TryGetValue(id,out var actor))return;
        foreach(var item in actor.Items)
            if(item.Saved.EquippedSlot==EquipmentSlot.Weapon && item.Saved.Condition is {Current:>0})
            {
                if(!_weaponUses.TryAdd((id,execution),new(item.Saved.InstanceId,item.Definition.Condition!.WearPerHit)))throw new InvalidOperationException("Duplicate weapon execution receipt.");
                _wearLedger.Retain(item.Saved);
                return;
            }
    }
    private void EndWeaponUse(NetworkEntityId id,ulong execution)
    {if(_weaponUses.Remove((id,execution),out var receipt))_wearLedger.Release(receipt.Item);}

    private void QueueWear(NetworkEntityId id,ulong execution)
    {
        if(!_weaponUses.TryGetValue((id,execution),out var receipt)||receipt.Worn)return;
        receipt.Worn=true;_wearLedger.Debit(receipt.Item,receipt.Wear);
    }

    internal void FlushWear(NetworkEntityId id)=>FlushWear(id,atPulse:false);
    private void FlushPulseWear(NetworkEntityId id)=>FlushWear(id,atPulse:true);
    private void FlushWear(NetworkEntityId id,bool atPulse)
    {
        if(!_wearPending.Contains(id) || !_actors.TryGetValue(id,out var actor) ||
            !atPulse && combat.TryGet(id,out var combatant) && combatant.Action is {Finished:false})return;
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
