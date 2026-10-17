using Npgsql;

namespace Content.Database;

public sealed partial class PostgresCharacterStore
{
    public async Task<IReadOnlyDictionary<Guid,long>> LoadItemWearAsync(IReadOnlyList<Guid> items,CancellationToken token)
    {
        ItemWearStorageValidation.Keys(items);var result=new Dictionary<Guid,long>();
        if(items.Count==0)return result;
        await using var connection=await _source.OpenConnectionAsync(token);
        await using var query=new NpgsqlCommand("SELECT instance_id,total_wear FROM project_g_item_weapon_wear WHERE instance_id=ANY($1)",connection);
        query.Parameters.AddWithValue(items.ToArray());
        await using var rows=await query.ExecuteReaderAsync(token);
        while(await rows.ReadAsync(token))result.Add(rows.GetGuid(0),rows.GetInt64(1));
        return result;
    }
    private static async Task WriteItemWearAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,
        IReadOnlyList<DatabaseItemWear> writes,CancellationToken token)
    {
        foreach(var write in writes)
        {
            await using var seed=new NpgsqlCommand("INSERT INTO project_g_item_weapon_wear(instance_id,total_wear) SELECT $1,0 WHERE EXISTS(SELECT 1 FROM project_g_item_owners WHERE instance_id=$1) ON CONFLICT(instance_id) DO NOTHING",connection,transaction);
            seed.Parameters.AddWithValue(write.ItemId);await seed.ExecuteNonQueryAsync(token);
            await using var query=new NpgsqlCommand("UPDATE project_g_item_weapon_wear SET total_wear=$1 WHERE instance_id=$2 AND total_wear=$3",connection,transaction);
            query.Parameters.AddWithValue(write.Total);query.Parameters.AddWithValue(write.ItemId);query.Parameters.AddWithValue(write.ExpectedTotal);
            if(await query.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("Item wear revision conflict; combat checkpoint aborted.");
        }
    }
}
