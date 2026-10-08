using Content.Database;
namespace Content.Server.Persistence;
public interface ISocialStore
{
    Task<SocialSession> OpenSocialAsync(CancellationToken token);
    Task SaveSocialCheckpointAsync(IReadOnlyList<CharacterSave> changes, WorldNodeSave? world, SocialSave social, CancellationToken token);
}
public abstract class SocialSession(long revision, IReadOnlyList<DatabaseSocialRow> rows) : IAsyncDisposable
{
    public long Revision { get; internal set; } = revision;
    public IReadOnlyList<DatabaseSocialRow> Rows { get; } = rows;
    public abstract ValueTask DisposeAsync();
}
public sealed record SocialSave(SocialSession Session, IReadOnlyList<DatabaseSocialRow> Rows);
