using Microsoft.Data.Sqlite;

namespace Content.Database;

public sealed partial class DevelopmentSqliteCharacterStore
{
    private static void WriteOwnedEchoChanges(SqliteConnection connection,SqliteTransaction transaction,Guid characterId,
        DatabaseEchoChanges changes,CancellationToken token)
    {
        EchoStorageValidation.Changes(changes);
        using var remove=connection.CreateCommand();remove.Transaction=transaction;
        remove.CommandText="DELETE FROM character_owned_echoes WHERE character_id=$owner AND instance_id=$instance";
        remove.Parameters.AddWithValue("$owner",characterId.ToString());var removed=remove.Parameters.Add("$instance",SqliteType.Text);remove.Prepare();
        foreach(var id in changes.Removed)
        {
            token.ThrowIfCancellationRequested();removed.Value=id.ToString();
            if(remove.ExecuteNonQuery()!=1)throw new InvalidOperationException("Removed Echo no longer belongs to this character.");
        }
        using var upsert=connection.CreateCommand();upsert.Transaction=transaction;
        upsert.CommandText="""
            INSERT INTO character_owned_echoes(character_id,instance_id,model_version,state) VALUES($owner,$instance,$version,$state)
            ON CONFLICT(character_id,instance_id) DO UPDATE SET model_version=excluded.model_version,state=excluded.state
            """;
        upsert.Parameters.AddWithValue("$owner",characterId.ToString());var instance=upsert.Parameters.Add("$instance",SqliteType.Text);
        var version=upsert.Parameters.Add("$version",SqliteType.Integer);var state=upsert.Parameters.Add("$state",SqliteType.Text);upsert.Prepare();
        foreach(var row in changes.Upserts)
        {
            token.ThrowIfCancellationRequested();instance.Value=row.InstanceId.ToString();version.Value=row.Version;state.Value=row.State;
            if(upsert.ExecuteNonQuery()!=1)throw new InvalidOperationException("Owned Echo upsert failed.");
        }
    }
    private static IReadOnlyList<DatabaseEchoRow> ReadOwnedEchoes(SqliteConnection connection, SqliteTransaction transaction, Guid characterId)
    {
        using var query = connection.CreateCommand(); query.Transaction = transaction;
        query.CommandText = "SELECT instance_id, model_version, state FROM character_owned_echoes WHERE character_id = $id ORDER BY instance_id";
        query.Parameters.AddWithValue("$id", characterId.ToString());
        using var reader = query.ExecuteReader();
        var rows = new List<DatabaseEchoRow>();
        while (reader.Read()) rows.Add(new(Guid.Parse(reader.GetString(0)), reader.GetInt32(1), reader.GetString(2)));
        EchoStorageValidation.Rows(rows);
        return rows.AsReadOnly();
    }

    // The caller has already fenced the parent revision in this same transaction.
    private static void WriteOwnedEchoes(SqliteConnection connection, SqliteTransaction transaction, Guid characterId,
        IReadOnlyList<DatabaseEchoRow> rows, CancellationToken token)
    {
        EchoStorageValidation.Rows(rows);
        using var query = connection.CreateCommand(); query.Transaction = transaction;
        query.CommandText = "DELETE FROM character_owned_echoes WHERE character_id = $id";
        query.Parameters.AddWithValue("$id", characterId.ToString()); query.ExecuteNonQuery();
        query.CommandText = "INSERT INTO character_owned_echoes VALUES ($id, $instance, $version, $state)";
        var instance = query.Parameters.Add("$instance", SqliteType.Text);
        var version = query.Parameters.Add("$version", SqliteType.Integer);
        var state = query.Parameters.Add("$state", SqliteType.Text);
        query.Prepare();
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            instance.Value = row.InstanceId.ToString(); version.Value = row.Version; state.Value = row.State;
            query.ExecuteNonQuery();
        }
    }
}
