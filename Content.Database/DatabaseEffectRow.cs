namespace Content.Database;

/// <summary>One opaque effect document. The collection has no gameplay count cap.</summary>
public sealed record DatabaseEffectRow(Guid InstanceId, int Version, string State);

internal static class EffectStorageValidation
{
    internal static void Rows(IReadOnlyList<DatabaseEffectRow> rows)
    {
        var ids = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row is null || row.InstanceId == Guid.Empty || row.Version != 1 || !ids.Add(row.InstanceId))
                throw new InvalidDataException("Invalid or duplicate effect document identity/version.");
            StorageBounds.Document(row.State);
        }
    }
}
