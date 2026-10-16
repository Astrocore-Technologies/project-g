using Npgsql;
using NpgsqlTypes;
namespace Content.Database;

public sealed partial class PostgresCharacterStore
{
    private static async Task<IReadOnlyList<DatabaseDeathLogRow>> ReadDeathLogsAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid character,CancellationToken token)
    {
        await using var query=new NpgsqlCommand("SELECT death_id,model_version,state::text FROM project_g_death_logs WHERE character_id=$1 ORDER BY death_id",connection,transaction);
        query.Parameters.AddWithValue(character);await using var reader=await query.ExecuteReaderAsync(token);var rows=new List<DatabaseDeathLogRow>();
        while(await reader.ReadAsync(token))
        {if(rows.Count==10)throw new InvalidDataException("Too many saved death logs.");rows.Add(new(checked((ulong)reader.GetInt64(0)),reader.GetInt32(1),reader.GetString(2)));}
        DeathLogStorageValidation.Rows(rows);return rows.AsReadOnly();
    }
    private static async Task WriteDeathLogsAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid character,
        IReadOnlyList<DatabaseDeathLogRow> rows,CancellationToken token)
    {
        DeathLogStorageValidation.Rows(rows);await using var query=new NpgsqlCommand("DELETE FROM project_g_death_logs WHERE character_id=$1",connection,transaction);
        query.Parameters.AddWithValue(character);await query.ExecuteNonQueryAsync(token);query.CommandText="INSERT INTO project_g_death_logs VALUES($1,$2,$3,$4)";
        var death=query.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Bigint});
        var version=query.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Integer});var state=query.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Jsonb});
        await query.PrepareAsync(token);
        foreach(var row in rows){death.Value=checked((long)row.DeathId);version.Value=row.Version;state.Value=row.State;await query.ExecuteNonQueryAsync(token);}
    }
}
