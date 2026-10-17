using Npgsql;
using NpgsqlTypes;

namespace Content.Database;

public sealed partial class PostgresCharacterStore
{
    private static async Task WriteOwnedEchoChangesAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid characterId,
        DatabaseEchoChanges changes,CancellationToken token)
    {
        EchoStorageValidation.Changes(changes);
        await using var remove=new NpgsqlCommand("DELETE FROM project_g_owned_echoes WHERE character_id=$1 AND instance_id=$2",connection,transaction);
        remove.Parameters.AddWithValue(characterId);var removed=remove.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Uuid});
        await remove.PrepareAsync(token);
        foreach(var id in changes.Removed)
        {
            removed.Value=id;
            if(await remove.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("Removed Echo no longer belongs to this character.");
        }
        await using var upsert=new NpgsqlCommand("""
            INSERT INTO project_g_owned_echoes(character_id,instance_id,model_version,state) VALUES($1,$2,$3,$4)
            ON CONFLICT(character_id,instance_id) DO UPDATE SET model_version=excluded.model_version,state=excluded.state
            """,connection,transaction);
        upsert.Parameters.AddWithValue(characterId);var instance=upsert.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Uuid});
        var version=upsert.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Integer});var state=upsert.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Jsonb});
        await upsert.PrepareAsync(token);
        foreach(var row in changes.Upserts)
        {
            instance.Value=row.InstanceId;version.Value=row.Version;state.Value=row.State;
            if(await upsert.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("Owned Echo upsert failed.");
        }
    }
    private static async Task<IReadOnlyList<DatabaseEchoRow>> ReadOwnedEchoesAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid characterId, CancellationToken token)
    {
        await using var query = new NpgsqlCommand(
            "SELECT instance_id, model_version, state::text FROM project_g_owned_echoes WHERE character_id = $1 ORDER BY instance_id", connection, transaction);
        query.Parameters.AddWithValue(characterId);
        await using var reader = await query.ExecuteReaderAsync(token);
        var rows = new List<DatabaseEchoRow>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2)));
        EchoStorageValidation.Rows(rows);
        return rows.AsReadOnly();
    }

    private static async Task WriteOwnedEchoesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid characterId, IReadOnlyList<DatabaseEchoRow> rows, CancellationToken token)
    {
        EchoStorageValidation.Rows(rows);
        await using var query = new NpgsqlCommand("DELETE FROM project_g_owned_echoes WHERE character_id = $1", connection, transaction);
        query.Parameters.AddWithValue(characterId); await query.ExecuteNonQueryAsync(token);
        query.CommandText = "INSERT INTO project_g_owned_echoes VALUES ($1, $2, $3, $4)";
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
