using Npgsql;
namespace Content.Database;
public sealed partial class PostgresCharacterStore
{
    public async Task EnsureInventoryOwnershipAsync(DatabaseSession lease,IReadOnlyList<Guid> items,CancellationToken token)
    {
        OwnershipValidation.Items(items,8); if(lease is not Session session) throw new InvalidOperationException("Foreign item owner.");
        await using(var probe=new NpgsqlCommand("SELECT 1",session.Connection)) await probe.ExecuteScalarAsync(token);
        await using var c=await _source.OpenConnectionAsync(token); await using var transaction=await c.BeginTransactionAsync(token);
        await using var fence=new NpgsqlCommand("SELECT 1 FROM project_g_characters WHERE character_id=$1 AND owner_id=$2 AND revision=$3 FOR UPDATE",c,transaction); fence.Parameters.AddWithValue(session.CharacterId); fence.Parameters.AddWithValue(session.OwnerId); fence.Parameters.AddWithValue(session.Revision);
        if(await fence.ExecuteScalarAsync(token) is null) throw new InvalidOperationException("Inventory ownership fence conflict.");
        foreach(var item in items.Order()) await EnsureItemOwnerAsync(c,transaction,item,"c:"+session.CharacterId,token);
        await transaction.CommitAsync(token);
    }
    private static async Task EnsureItemOwnerAsync(NpgsqlConnection c,NpgsqlTransaction transaction,Guid item,string owner,CancellationToken token)
    {
        await using var q=new NpgsqlCommand("INSERT INTO project_g_item_owners VALUES($1,$2) ON CONFLICT(instance_id) DO NOTHING",c,transaction); q.Parameters.AddWithValue(item); q.Parameters.AddWithValue(owner); await q.ExecuteNonQueryAsync(token); q.CommandText="SELECT owner_key FROM project_g_item_owners WHERE instance_id=$1"; q.Parameters.RemoveAt(1);
        if(await q.ExecuteScalarAsync(token) is not string actual || actual!=owner) throw new InvalidOperationException("Item already belongs to another owner.");
    }
    private static async Task SyncOwnershipAsync(NpgsqlConnection c,NpgsqlTransaction transaction,IReadOnlyList<DatabaseSave> changes,IReadOnlyList<DatabaseWorldSave> worlds,CancellationToken token)
    {
        var sets=OwnershipValidation.Sets(changes,worlds); var prior=new HashSet<Guid>(); var retained=sets.Values.SelectMany(ids=>ids).ToHashSet();
        foreach(var owner in sets.Keys.Order()) { await using var q=new NpgsqlCommand("SELECT instance_id FROM project_g_item_owners WHERE owner_key=$1 FOR UPDATE",c,transaction); q.Parameters.AddWithValue(owner); await using var r=await q.ExecuteReaderAsync(token); while(await r.ReadAsync(token)) prior.Add(r.GetGuid(0)); }
        foreach(var owner in sets.Keys.Order()) { await using var q=new NpgsqlCommand("DELETE FROM project_g_item_owners WHERE owner_key=$1",c,transaction); q.Parameters.AddWithValue(owner); await q.ExecuteNonQueryAsync(token); }
        foreach(var change in changes) if(change.GroundClaims is { } claims) foreach(var item in claims)
        { await using var q=new NpgsqlCommand("DELETE FROM project_g_item_owners WHERE instance_id=$1 AND owner_key=$2",c,transaction); q.Parameters.AddWithValue(item); q.Parameters.AddWithValue("g:"+item); await q.ExecuteNonQueryAsync(token); }
        foreach(var removed in prior.Order()) if(!retained.Contains(removed)) await EnsureItemOwnerAsync(c,transaction,removed,"d:"+removed,token);
        foreach(var (owner,items) in sets.OrderBy(pair=>pair.Key)) foreach(var item in items.Order())
        { await using var q=new NpgsqlCommand("INSERT INTO project_g_item_owners VALUES($1,$2)",c,transaction); q.Parameters.AddWithValue(item); q.Parameters.AddWithValue(owner); await q.ExecuteNonQueryAsync(token); }
    }
}
