using Content.Database;

namespace Content.Server.Persistence;

/// <summary>Domain validation stays on the server; Database stores bounded documents.</summary>
public sealed class DatabaseCharacterStore(ICharacterDatabase database) : ICharacterStore
{
    public Task InitializeAsync(CancellationToken token) => database.InitializeAsync(token);
    public async Task<CharacterSession> OpenAsync(string credential, CharacterState initial, CancellationToken token)
    {
        DatabaseSession lease;
        try { lease = await database.OpenAsync(credential, initial.Serialize(), (initial.Inventory ?? SavedInventory.Empty).Serialize(), token); }
        catch (DatabaseInvalidIdentityException) { throw new InvalidIdentityException(); }
        catch (DatabaseCharacterInUseException) { throw new CharacterInUseException(); }
        try
        {
            var state = CharacterState.Deserialize(lease.State) with { Inventory = SavedInventory.Deserialize(lease.Inventory) };
            return new Session(lease, state);
        }
        catch { await lease.DisposeAsync(); throw; }
    }
    public Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken token)
    {
        var writes = new DatabaseSave[changes.Count];
        for (var i = 0; i < writes.Length; i++)
        {
            var change = changes[i];
            if (change.Session is not Session session) throw new ArgumentException("Session belongs to another store.");
            writes[i] = new(session.Lease, session.Revision, change.State.Serialize(), change.State.Inventory?.Serialize());
        }
        return database.SaveAsync(writes, token);
    }
    private sealed class Session(DatabaseSession lease, CharacterState state)
        : CharacterSession(lease.CharacterId, lease.OwnerId, lease.Revision, state, lease.IssuedToken)
    {
        internal DatabaseSession Lease { get; } = lease;
        public override ValueTask DisposeAsync() => Lease.DisposeAsync();
    }
}
