using Microsoft.Data.Sqlite;

namespace Content.Database;

public sealed partial class DevelopmentSqliteCharacterStore
{
    private static IReadOnlyList<DatabaseEffectRow> ReadEffects(SqliteConnection connection, SqliteTransaction transaction, Guid characterId)
    {
        using var query = connection.CreateCommand(); query.Transaction = transaction;
        query.CommandText = "SELECT instance_id, model_version, state FROM character_effects WHERE character_id = $id ORDER BY instance_id";
        query.Parameters.AddWithValue("$id", characterId.ToString());
        using var reader = query.ExecuteReader();
        var rows = new List<DatabaseEffectRow>();
        while (reader.Read()) rows.Add(new(Guid.Parse(reader.GetString(0)), reader.GetInt32(1), reader.GetString(2)));
        EffectStorageValidation.Rows(rows);
        return rows.AsReadOnly();
    }

    // The caller has already fenced the parent revision in this same transaction.
    private static void WriteEffects(SqliteConnection connection, SqliteTransaction transaction, Guid characterId,
        IReadOnlyList<DatabaseEffectRow> rows, CancellationToken token)
    {
        EffectStorageValidation.Rows(rows);
        using var query = connection.CreateCommand(); query.Transaction = transaction;
        query.CommandText = "DELETE FROM character_effects WHERE character_id = $id";
        query.Parameters.AddWithValue("$id", characterId.ToString()); query.ExecuteNonQuery();
        query.CommandText = "INSERT INTO character_effects VALUES ($id, $instance, $version, $state)";
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
