namespace Content.Database;

public sealed partial class DevelopmentSqliteCharacterStore
{
    public Task<IReadOnlyList<DatabaseGroundItem>> LoadGroundItemsAsync(IReadOnlyList<DatabaseGroundItem> seeds, CancellationToken token) =>
        Task.Run<IReadOnlyList<DatabaseGroundItem>>(() =>
        {
            if (seeds.Count > 256) throw new InvalidDataException("Ground seed budget exceeded.");
            using var connection = Connect(); using var transaction = connection.BeginTransaction();
            foreach (var seed in seeds)
            {
                token.ThrowIfCancellationRequested(); seed.Validate();
                using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                // Seed identity is stable across restarts; claimed rows remain as tombstones (no respawn).
                insert.CommandText = "INSERT INTO ground_items VALUES($id,$seed,$definition,$x,$z,NULL) ON CONFLICT(seed_id) DO NOTHING";
                insert.Parameters.AddWithValue("$id", seed.InstanceId.ToString()); insert.Parameters.AddWithValue("$seed", seed.SeedId);
                insert.Parameters.AddWithValue("$definition", seed.DefinitionId); insert.Parameters.AddWithValue("$x", seed.X); insert.Parameters.AddWithValue("$z", seed.Z);
                insert.ExecuteNonQuery();
            }
            using var read = connection.CreateCommand(); read.Transaction = transaction;
            read.CommandText = "SELECT instance_id,seed_id,definition_id,x,z FROM ground_items WHERE claimed_by IS NULL ORDER BY seed_id LIMIT 257";
            using var reader = read.ExecuteReader(); var items = new List<DatabaseGroundItem>();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (items.Count == 256) throw new InvalidDataException("Ground item budget exceeded.");
                var item = new DatabaseGroundItem(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetFloat(3), reader.GetFloat(4));
                item.Validate(); items.Add(item);
            }
            reader.Dispose(); token.ThrowIfCancellationRequested(); transaction.Commit(); return items;
        }, token);
}
