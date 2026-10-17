namespace Content.Server.Persistence;

public interface ICharacterStore : ICombatCheckpointStore
{
    Task ICombatCheckpointStore.SaveCombatCheckpointAsync(IReadOnlyList<CharacterSave> changes,IReadOnlyList<WorldNodeSave> worlds,
        SocialSave? social,IReadOnlyList<ItemWearChange> wear,CancellationToken token)
    {
        // Legacy adapters may persist a debit carried by a full inventory/escrow image.
        // Detached/offline writes require the explicit ledger implementation.
        foreach(var write in wear)
        {
            var represented=changes.Any(c=>c.State.Inventory?.Items.Any(i=>i.InstanceId==write.ItemId&&i.Condition?.AppliedWeaponWear>=write.Total)==true)||
                worlds.Any(w=>(w.State.DeathLoot?.Any(l=>l.Item.InstanceId==write.ItemId&&l.Item.Condition?.AppliedWeaponWear>=write.Total)==true)||
                    (w.State.Market?.Listings.Any(l=>l.Item.InstanceId==write.ItemId&&l.Item.Condition?.AppliedWeaponWear>=write.Total)==true));
            if(!represented)throw new NotSupportedException("This adapter cannot persist detached item wear.");
        }
        if(this is IRegionalCharacterStore regional)return regional.SaveRegionalCheckpointAsync(changes,worlds,social,token);
        if(worlds.Count>1)throw new NotSupportedException("Regional storage required.");
        if(social is not null)return (this as ISocialStore??throw new NotSupportedException("Social storage required."))
            .SaveSocialCheckpointAsync(changes,worlds.SingleOrDefault(),social,token);
        return SaveWithWorldAsync(changes,worlds.SingleOrDefault(),token);
    }
    Task<WorldNodeSession> OpenWorldAsync(string key,SavedWorldNode initial,CancellationToken token) => throw new NotSupportedException("World ownership required.");
    Task SaveWithWorldAsync(IReadOnlyList<CharacterSave> changes,WorldNodeSave? world,CancellationToken token) => world is null ? SaveAsync(changes,token) : throw new NotSupportedException("Atomic world save required.");
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<CharacterSession> OpenAsync(string token, CharacterState initial, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedGroundItem>> LoadGroundItemsAsync(IReadOnlyList<SavedGroundItem> seeds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Ground item persistence is required for pickup.");
}

/// <summary>Exclusive ownership lasts through the final save and asynchronous disposal.</summary>
public abstract class CharacterSession(Guid characterId, Guid ownerId, long revision, CharacterState state, string issuedToken)
    : IAsyncDisposable
{
    public Guid CharacterId { get; } = characterId;
    public Guid OwnerId { get; } = ownerId;
    public long Revision { get; internal set; } = revision;
    public CharacterState State { get; } = state;
    public string IssuedToken { get; } = issuedToken;
    public abstract ValueTask DisposeAsync();
}

public sealed record CharacterSave(CharacterSession Session, CharacterState State, IReadOnlyList<Guid>? GroundClaims = null);
public sealed class InvalidIdentityException : Exception;
public sealed class CharacterInUseException : Exception;
