using Content.Server.Persistence;
using Content.Shared.Network;

namespace Content.Server.Items;

public sealed partial class InventorySimulation
{
    private ItemWearLedger _wearLedger=new();
    private readonly Dictionary<Guid,(NetworkEntityId Owner,Item Item)> _itemLocations=new();
    internal ItemWearLedger WearLedger=>_wearLedger;
    internal void UseWearLedger(ItemWearLedger ledger)
    {
        if(ReferenceEquals(ledger,_wearLedger))return;
        if(!_wearLedger.IsEmpty)throw new InvalidOperationException("Compose the regional item ledger before starting combat.");
        _wearLedger.Detach(ApplyWeaponWear);_wearLedger=ledger;_wearLedger.Attach(ApplyWeaponWear);
    }
    private void UnindexItems(NetworkEntityId id,Actor actor)
    {
        foreach(var uuid in actor.IndexedItems)
            if(_itemLocations.TryGetValue(uuid,out var location)&&location.Owner==id)_itemLocations.Remove(uuid);
        actor.IndexedItems=[];
    }
    private void IndexItems(NetworkEntityId id,Actor actor)
    {
        UnindexItems(id,actor);actor.IndexedItems=new Guid[actor.Items.Length];
        for(var i=0;i<actor.Items.Length;i++)
        {
            var item=actor.Items[i];var synchronized=_wearLedger.Synchronize(item.Saved);
            if(!ReferenceEquals(synchronized,item.Saved)){item.Saved=synchronized;_dirty.Add(id);}
            if(item.Saved.Condition is {PendingWear:>0})_wearPending.Add(id);
            actor.IndexedItems[i]=item.Saved.InstanceId;
            if(!_itemLocations.TryAdd(item.Saved.InstanceId,(id,item)))throw new InvalidDataException("Duplicate live item UUID.");
        }
    }
    private bool ApplyWeaponWear(Guid uuid,long total)
    {
        if(!_itemLocations.TryGetValue(uuid,out var location))return false;
        var item=location.Item;item.Saved=ItemWearDocuments.Apply(item.Saved,total);
        _wearPending.Add(location.Owner);_dirty.Add(location.Owner);return true;
    }
}
