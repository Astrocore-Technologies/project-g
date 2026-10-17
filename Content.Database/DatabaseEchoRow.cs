namespace Content.Database;

/// <summary>One owned companion document; active body limits do not limit ownership.</summary>
public sealed record DatabaseEchoRow(Guid InstanceId, int Version, string State);
public sealed record DatabaseEchoChanges(IReadOnlyList<DatabaseEchoRow> Upserts,IReadOnlyList<Guid> Removed);

internal static class EchoStorageValidation
{
    internal static void Changes(DatabaseEchoChanges changes)
    {
        if(changes.Upserts is null||changes.Removed is null)throw new InvalidDataException("Missing owned Echo changes.");
        Rows(changes.Upserts);var ids=new HashSet<Guid>(changes.Upserts.Select(row=>row.InstanceId));
        foreach(var id in changes.Removed)
            if(id==Guid.Empty||!ids.Add(id))throw new InvalidDataException("Invalid, duplicate or conflicting removed Echo.");
    }
    internal static void Rows(IReadOnlyList<DatabaseEchoRow> rows)
    {
        var ids = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row is null || row.InstanceId == Guid.Empty || row.Version != 1 || !ids.Add(row.InstanceId))
                throw new InvalidDataException("Invalid or duplicate owned Echo identity/version.");
            StorageBounds.Document(row.State);
        }
    }
}
