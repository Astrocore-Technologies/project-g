using Microsoft.Data.Sqlite;

namespace Content.Database;

public sealed partial class DevelopmentSqliteCharacterStore
{
    public Task<IReadOnlyDictionary<Guid,long>> LoadItemWearAsync(IReadOnlyList<Guid> items,CancellationToken token)=>
        Task.Run<IReadOnlyDictionary<Guid,long>>(()=>
        {
            ItemWearStorageValidation.Keys(items);var result=new Dictionary<Guid,long>();
            using var connection=Connect();using var query=connection.CreateCommand();
            query.CommandText="SELECT total_wear FROM item_weapon_wear WHERE instance_id=$item";
            var parameter=query.Parameters.Add("$item",SqliteType.Text);
            foreach(var item in items)
            {
                token.ThrowIfCancellationRequested();parameter.Value=item.ToString();
                if(query.ExecuteScalar() is long total)result.Add(item,total);
            }
            return result;
        },token);

    private static void WriteItemWear(SqliteConnection connection,SqliteTransaction transaction,
        IReadOnlyList<DatabaseItemWear> writes,CancellationToken token)
    {
        foreach(var write in writes)
        {
            token.ThrowIfCancellationRequested();using var query=connection.CreateCommand();query.Transaction=transaction;
            query.CommandText="INSERT INTO item_weapon_wear(instance_id,total_wear) SELECT $item,0 WHERE EXISTS(SELECT 1 FROM item_owners WHERE instance_id=$item) ON CONFLICT(instance_id) DO NOTHING";
            query.Parameters.AddWithValue("$item",write.ItemId.ToString());query.ExecuteNonQuery();
            query.CommandText="UPDATE item_weapon_wear SET total_wear=$total WHERE instance_id=$item AND total_wear=$expected";
            query.Parameters.AddWithValue("$total",write.Total);query.Parameters.AddWithValue("$expected",write.ExpectedTotal);
            if(query.ExecuteNonQuery()!=1)throw new InvalidOperationException("Item wear revision conflict; combat checkpoint aborted.");
        }
    }
}
