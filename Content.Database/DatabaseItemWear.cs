namespace Content.Database;

/// <summary>Monotonic instance-bound wear; no ownership or game rules are stored here.</summary>
public sealed record DatabaseItemWear(Guid ItemId,long ExpectedTotal,long Total);

internal static class ItemWearStorageValidation
{
    internal static void Keys(IReadOnlyList<Guid> items)
    {if(items.Count>64||items.Any(id=>id==Guid.Empty)||items.Distinct().Count()!=items.Count)throw new InvalidDataException("Invalid item wear query.");}
    internal static void Writes(IReadOnlyList<DatabaseItemWear> writes)
    {
        if(writes.Count>1024||writes.Any(w=>w is null||w.ItemId==Guid.Empty||w.ExpectedTotal<0||w.Total<=w.ExpectedTotal)||
            writes.Select(w=>w.ItemId).Distinct().Count()!=writes.Count)throw new InvalidDataException("Invalid item wear checkpoint.");
    }
}
