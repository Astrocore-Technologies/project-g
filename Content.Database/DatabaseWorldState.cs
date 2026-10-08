namespace Content.Database;
public abstract class DatabaseWorldSession(string key, Guid ownerId, long revision, string state) : IAsyncDisposable
{
    public string Key { get; } = key;
    public Guid OwnerId { get; } = ownerId;
    public long Revision { get; } = revision;
    public string State { get; } = state;
    public abstract ValueTask DisposeAsync();
}
public sealed record DatabaseWorldSave(DatabaseWorldSession Session, long ExpectedRevision, string State, IReadOnlyList<DatabaseWorldAudit> Audit, IReadOnlyList<Guid>? EscrowItems = null);
public sealed record DatabaseWorldAudit(string Actor, string Operation, string Reason, long Timestamp);
