namespace Content.Database;

/// <summary>Opaque versioned documents: no gameplay, Godot or wire dependencies.</summary>
public interface ICharacterDatabase
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<DatabaseSession> OpenAsync(string token, string initialCharacter, string initialInventory, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<DatabaseSave> changes, CancellationToken cancellationToken);
}
public abstract class DatabaseSession(Guid characterId, Guid ownerId, long revision, string state, string inventory, string issuedToken) : IAsyncDisposable
{
    public Guid CharacterId { get; } = characterId;
    public Guid OwnerId { get; } = ownerId;
    public long Revision { get; } = revision;
    public string State { get; } = state;
    public string Inventory { get; } = inventory;
    public string IssuedToken { get; } = issuedToken;
    public abstract ValueTask DisposeAsync();
}
public sealed record DatabaseSave(DatabaseSession Session, long ExpectedRevision, string State, string? Inventory);
public sealed class DatabaseInvalidIdentityException : Exception;
public sealed class DatabaseCharacterInUseException : Exception;
internal static class StorageBounds
{
    internal static bool ValidToken(string token) => token.Length == 0 || token.Length == 64 &&
        token.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    internal static void Document(string value)
    {
        if (string.IsNullOrEmpty(value) || System.Text.Encoding.UTF8.GetByteCount(value) > 8192)
            throw new InvalidDataException("Stored document exceeds size budget.");
    }
}
