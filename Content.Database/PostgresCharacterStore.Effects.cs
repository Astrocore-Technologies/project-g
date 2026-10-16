using Npgsql;
using NpgsqlTypes;

namespace Content.Database;

public sealed partial class PostgresCharacterStore
{
    private static async Task<IReadOnlyList<DatabaseEffectRow>> ReadEffectsAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid characterId, CancellationToken token)
    {
        await using var query = new NpgsqlCommand(
            "SELECT instance_id, model_version, state::text FROM project_g_effects WHERE character_id = $1 ORDER BY instance_id", connection, transaction);
        query.Parameters.AddWithValue(characterId);
        await using var reader = await query.ExecuteReaderAsync(token);
        var rows = new List<DatabaseEffectRow>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2)));
        EffectStorageValidation.Rows(rows);
        return rows.AsReadOnly();
    }

    private static async Task WriteEffectsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid characterId, IReadOnlyList<DatabaseEffectRow> rows, CancellationToken token)
    {
        EffectStorageValidation.Rows(rows);
        await using var query = new NpgsqlCommand("DELETE FROM project_g_effects WHERE character_id = $1", connection, transaction);
        query.Parameters.AddWithValue(characterId); await query.ExecuteNonQueryAsync(token);
        query.CommandText = "INSERT INTO project_g_effects VALUES ($1, $2, $3, $4)";
        var instance = query.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid });
        var version = query.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer });
        var state = query.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb });
        await query.PrepareAsync(token);
        foreach (var row in rows)
        {
            instance.Value = row.InstanceId; version.Value = row.Version; state.Value = row.State;
            await query.ExecuteNonQueryAsync(token);
        }
    }
}
