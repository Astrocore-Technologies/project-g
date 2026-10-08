namespace Content.Database;
internal static class OwnershipValidation
{
    internal static void Items(IReadOnlyList<Guid> items,int maximum)
    { if(items.Count>maximum || items.Any(id=>id==Guid.Empty) || items.Distinct().Count()!=items.Count) throw new InvalidDataException("Invalid ownership item set."); }
    internal static Dictionary<string,IReadOnlyList<Guid>> Sets(IReadOnlyList<DatabaseSave> changes,IReadOnlyList<DatabaseWorldSave> worlds)
    {
        if(changes.Count>64) throw new InvalidDataException("Ownership batch exceeds session budget.");
        var sets=new Dictionary<string,IReadOnlyList<Guid>>();
        foreach(var change in changes) if(change.InventoryItems is { } items)
        { if(change.Inventory is null) throw new InvalidDataException("Ownership needs inventory write."); Items(items,8); if(!sets.TryAdd("c:"+change.Session.CharacterId,items)) throw new InvalidDataException("Duplicate owner write."); }
        foreach (var world in worlds)
            if(world.EscrowItems is { } escrow) { Items(escrow,16); sets.Add("w:"+world.Session.Key,escrow); }
        return sets;
    }
}
