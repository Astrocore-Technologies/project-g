using Npgsql;

namespace Content.Database;

public sealed partial class PostgresCharacterStore
{
    public async Task<IReadOnlyList<DatabaseGroundItem>> LoadGroundItemsAsync(IReadOnlyList<DatabaseGroundItem> seeds, CancellationToken token)
    {
        if (seeds.Count > 256) throw new InvalidDataException("Ground seed budget exceeded.");
        await using var connection = await _source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        foreach (var seed in seeds)
        {
            seed.Validate();
            await using var insert = new NpgsqlCommand("INSERT INTO project_g_ground_items VALUES($1,$2,$3,$4,$5,NULL) ON CONFLICT(seed_id) DO NOTHING", connection, transaction);
            insert.Parameters.AddWithValue(seed.InstanceId); insert.Parameters.AddWithValue(seed.SeedId); insert.Parameters.AddWithValue(seed.DefinitionId);
            insert.Parameters.AddWithValue(seed.X); insert.Parameters.AddWithValue(seed.Z); await insert.ExecuteNonQueryAsync(token);
        }
        await using var read = new NpgsqlCommand("SELECT instance_id,seed_id,definition_id,x,z FROM project_g_ground_items WHERE claimed_by IS NULL ORDER BY seed_id LIMIT 257", connection, transaction);
        await using var reader = await read.ExecuteReaderAsync(token); var items = new List<DatabaseGroundItem>();
        while (await reader.ReadAsync(token))
        {
            if (items.Count == 256) throw new InvalidDataException("Ground item budget exceeded.");
            var item = new DatabaseGroundItem(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFloat(3), reader.GetFloat(4));
            item.Validate(); items.Add(item);
        }
        await reader.DisposeAsync(); await transaction.CommitAsync(token); return items;
    }
}
