using Content.Database;

namespace Content.Server.Persistence;

/// <summary>Domain validation stays on the server; Database stores bounded documents.</summary>
public sealed class DatabaseCharacterStore(ICharacterDatabase database) : ICharacterStore, ISocialStore
{
    public async Task<WorldNodeSession> OpenWorldAsync(string key,SavedWorldNode initial,CancellationToken token)
    {
        var lease=await database.OpenWorldAsync(key,initial.Serialize(),token);
        try { return new WorldSession(lease,SavedWorldNode.Deserialize(lease.State)); }
        catch { await lease.DisposeAsync(); throw; }
    }
    private sealed class WorldSession(DatabaseWorldSession lease,SavedWorldNode state) : WorldNodeSession(lease.Revision,state)
    {
        internal DatabaseWorldSession Lease { get; }=lease;
        public override ValueTask DisposeAsync() => Lease.DisposeAsync();
    }
    public async Task<SocialSession> OpenSocialAsync(CancellationToken token)
    {
        var lease = await database.OpenWorldAsync("social_prototype", "{}", token);
        try { return new SocialLease(lease, await database.LoadSocialAsync("social_prototype", token)); }
        catch { await lease.DisposeAsync(); throw; }
    }
    private sealed class SocialLease(DatabaseWorldSession lease, IReadOnlyList<DatabaseSocialRow> rows) : SocialSession(lease.Revision, rows)
    {
        internal DatabaseWorldSession Lease { get; } = lease;
        public override ValueTask DisposeAsync() => Lease.DisposeAsync();
    }
    public Task SaveSocialCheckpointAsync(IReadOnlyList<CharacterSave> changes, WorldNodeSave? world, SocialSave social, CancellationToken token) => SaveCheckpoint(changes, world, social, token);
    public Task InitializeAsync(CancellationToken token) => database.InitializeAsync(token);
    public async Task<IReadOnlyList<SavedGroundItem>> LoadGroundItemsAsync(IReadOnlyList<SavedGroundItem> seeds, CancellationToken token)
    {
        var rows = await database.LoadGroundItemsAsync(seeds.Select(item => new DatabaseGroundItem(
            item.InstanceId, item.SeedId, item.DefinitionId, item.X, item.Z)).ToArray(), token);
        return rows.Select(item => new SavedGroundItem(item.InstanceId, item.SeedId, item.DefinitionId, item.X, item.Z)).ToArray();
    }
    public async Task<CharacterSession> OpenAsync(string credential, CharacterState initial, CancellationToken token)
    {
        DatabaseSession lease;
        try { lease = await database.OpenAsync(credential, initial.Serialize(), (initial.Inventory ?? SavedInventory.Empty).Serialize(),
            (initial.Echoes ?? SavedEchoes.Empty).Serialize(), token, initial.Progression?.Serialize(),initial.Inventory?.Items.Select(i=>i.InstanceId).ToArray() ?? []); }
        catch (DatabaseInvalidIdentityException) { throw new InvalidIdentityException(); }
        catch (DatabaseCharacterInUseException) { throw new CharacterInUseException(); }
        try
        {
            var state = CharacterState.Deserialize(lease.State) with { Inventory = SavedInventory.Deserialize(lease.Inventory), Echoes = SavedEchoes.Deserialize(lease.Echoes), Progression = lease.Progression is null ? null : SavedProgression.Deserialize(lease.Progression) };
            await database.EnsureInventoryOwnershipAsync(lease,state.Inventory.Items.Select(i=>i.InstanceId).ToArray(),token);
            return new Session(lease, state);
        }
        catch { await lease.DisposeAsync(); throw; }
    }
    public Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken token) => SaveWithWorldAsync(changes,null,token);
    public Task SaveWithWorldAsync(IReadOnlyList<CharacterSave> changes,WorldNodeSave? world,CancellationToken token)
    => SaveCheckpoint(changes,world,null,token);
    private Task SaveCheckpoint(IReadOnlyList<CharacterSave> changes,WorldNodeSave? world,SocialSave? social,CancellationToken token)
    {
        var writes = new DatabaseSave[changes.Count];
        for (var i = 0; i < writes.Length; i++)
        {
            var change = changes[i];
            if (change.Session is not Session session) throw new ArgumentException("Session belongs to another store.");
            if (change.GroundClaims is { Count: > 0 } claims)
                foreach (var instance in claims)
                    if (change.State.Inventory is null || !change.State.Inventory.Items.Any(item => item.InstanceId == instance && item.EquippedSlot == Content.Shared.Network.EquipmentSlot.None))
                        throw new InvalidDataException("Picked instance missing from inventory.");
            writes[i] = new(session.Lease, session.Revision, change.State.Serialize(), change.State.Inventory?.Serialize(), change.GroundClaims, change.State.Echoes?.Serialize(), change.State.Progression?.Serialize(),change.State.Inventory?.Items.Select(i=>i.InstanceId).ToArray());
        }
        DatabaseWorldSave? worldWrite=null;
        if (world is not null)
        {
            if (world.Session is not WorldSession owner) throw new ArgumentException("Foreign world session.");
            worldWrite=new(owner.Lease,owner.Revision,world.State.Serialize(),world.Audit,(world.State.Market?.Listings.Select(l=>l.Item.InstanceId) ?? []).Concat(world.State.DeathLoot?.Select(l=>l.Item.InstanceId) ?? []).ToArray());
        }
        DatabaseSocialSave? socialWrite = null;
        if (social is not null)
        {
            if (social.Session is not SocialLease owner) throw new ArgumentException("Foreign social session.");
            socialWrite = new(new(owner.Lease, owner.Revision, "{}", [new("social", "SocialCheckpoint", "Rows " + social.Rows.Count, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())], []), social.Rows);
        }
        return database.SaveCheckpointAsync(writes,worldWrite,socialWrite,token);
    }
    private sealed class Session(DatabaseSession lease, CharacterState state)
        : CharacterSession(lease.CharacterId, lease.OwnerId, lease.Revision, state, lease.IssuedToken)
    {
        internal DatabaseSession Lease { get; } = lease;
        public override ValueTask DisposeAsync() => Lease.DisposeAsync();
    }
}
