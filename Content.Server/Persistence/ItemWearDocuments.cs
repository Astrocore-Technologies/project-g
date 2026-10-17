namespace Content.Server.Persistence;

public sealed record ItemWearChange(Guid ItemId,long ExpectedTotal,long Total);

internal static class ItemWearDocuments
{
    internal static SavedItem Apply(SavedItem item,long total)
    {
        if(total<0)throw new InvalidDataException("Negative item wear total.");
        if(item.Condition is not {} state)
        {if(total>0)throw new InvalidDataException("Weapon wear requires an item condition.");return item;}
        if(total<state.AppliedWeaponWear)throw new InvalidDataException("Durable item wear watermark cannot move backwards.");
        if(total==state.AppliedWeaponWear)return item;
        var debit=(int)Math.Min(state.Current-state.PendingWear,total-state.AppliedWeaponWear);
        return item with {Condition=state with {PendingWear=state.PendingWear+debit,AppliedWeaponWear=total}};
    }
    internal static SavedItem Settle(SavedItem item)
    {
        if(item.Condition is not {PendingWear:>0} state)return item;
        if(state.Revision==long.MaxValue)throw new InvalidDataException("Item revision exhausted.");
        return item with {Condition=state with {Current=state.Current-state.PendingWear,PendingWear=0,Revision=state.Revision+1}};
    }
}

public interface ICombatCheckpointStore
{
    Task SaveCombatCheckpointAsync(IReadOnlyList<CharacterSave> characters,IReadOnlyList<WorldNodeSave> worlds,
        SocialSave? social,IReadOnlyList<ItemWearChange> wear,CancellationToken token);
}
