using Content.Server.Persistence;

namespace Content.Server.Items;

/// <summary>One simulation-thread ledger shared by regions. Only live executions and uncommitted writes are retained.</summary>
internal sealed class ItemWearLedger
{
    private sealed class Entry(long total)
    {
        internal long Committed=total,Total=total;
        internal int Executions;
    }
    private readonly Dictionary<Guid,Entry> _items=new();
    private readonly List<Func<Guid,long,bool>> _locations=new();
    private readonly HashSet<Guid> _dirty=new();
    private int _loads;
    internal bool HasWrites=>_dirty.Count>0;
    internal bool IsEmpty=>_items.Count==0;
    internal void Attach(Func<Guid,long,bool> location)=>_locations.Add(location);
    internal void Detach(Func<Guid,long,bool> location)=>_locations.Remove(location);
    internal IDisposable BeginLoad(){_loads++;return new Loading(this);}
    private sealed class Loading(ItemWearLedger ledger):IDisposable
    {
        private ItemWearLedger? _ledger=ledger;
        public void Dispose()
        {
            if(_ledger is not {} owner)return;_ledger=null;
            if(--owner._loads!=0)return;
            foreach(var id in owner._items.Keys.ToArray())owner.Prune(id,owner._items[id]);
        }
    }

    internal SavedItem Synchronize(SavedItem item)=>_items.TryGetValue(item.InstanceId,out var entry)
        ? ItemWearDocuments.Apply(item,entry.Total):item;

    internal void Retain(SavedItem item)
    {
        var applied=item.Condition?.AppliedWeaponWear??0;
        if(!_items.TryGetValue(item.InstanceId,out var entry))_items.Add(item.InstanceId,entry=new(applied));
        if(applied>entry.Total)throw new InvalidDataException("Item wear watermark is ahead of its live execution ledger.");
        entry.Executions=checked(entry.Executions+1);
    }
    internal void Release(Guid item)
    {
        if(!_items.TryGetValue(item,out var entry)||entry.Executions<=0)throw new InvalidOperationException("Unbalanced weapon execution receipt.");
        entry.Executions--;Prune(item,entry);
    }
    internal void Debit(Guid item,int amount)
    {
        if(amount<=0)return;
        var entry=_items[item];entry.Total=checked(entry.Total+amount);_dirty.Add(item);
        foreach(var location in _locations)if(location(item,entry.Total))return;
        // An offline owner is updated from this durable total at its next load.
    }
    internal ItemWearChange[] Capture()=>_dirty.Order().Select(id=>new ItemWearChange(id,_items[id].Committed,_items[id].Total)).ToArray();
    internal void Commit(IReadOnlyList<ItemWearChange> writes)
    {
        foreach(var write in writes)
        {
            var entry=_items[write.ItemId];
            if(entry.Committed!=write.ExpectedTotal||entry.Total<write.Total)throw new InvalidOperationException("Item wear checkpoint mismatch.");
            entry.Committed=write.Total;if(entry.Committed==entry.Total)_dirty.Remove(write.ItemId);
            Prune(write.ItemId,entry);
        }
    }
    private void Prune(Guid id,Entry entry)
    {if(_loads==0&&entry.Executions==0&&entry.Committed==entry.Total)_items.Remove(id);}
}
