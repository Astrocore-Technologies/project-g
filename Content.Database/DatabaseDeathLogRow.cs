namespace Content.Database;

public sealed record DatabaseDeathLogRow(ulong DeathId,int Version,string State);

internal static class DeathLogStorageValidation
{
    internal static void Rows(IReadOnlyList<DatabaseDeathLogRow> rows)
    {
        if(rows.Count>10)throw new InvalidDataException("Too many death logs.");
        var ids=new HashSet<ulong>();
        foreach(var row in rows)
            if(row is null || row.DeathId is 0 or >long.MaxValue || row.Version!=1 || !ids.Add(row.DeathId) ||
                string.IsNullOrEmpty(row.State) || System.Text.Encoding.UTF8.GetByteCount(row.State)>262144)
                throw new InvalidDataException("Invalid death log identity/version/size.");
    }
}
