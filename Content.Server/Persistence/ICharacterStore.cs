namespace Content.Server.Persistence;

public interface ICharacterStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<CharacterSession> OpenAsync(string token, CharacterState initial, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken cancellationToken);
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

public sealed record CharacterSave(CharacterSession Session, CharacterState State);
public sealed class InvalidIdentityException : Exception;
public sealed class CharacterInUseException : Exception;
