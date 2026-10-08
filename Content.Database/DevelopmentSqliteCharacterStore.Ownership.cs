using Microsoft.Data.Sqlite;
namespace Content.Database;
public sealed partial class DevelopmentSqliteCharacterStore
{
    public Task EnsureInventoryOwnershipAsync(DatabaseSession lease,IReadOnlyList<Guid> items,CancellationToken token) => Task.Run(() =>
    {
        OwnershipValidation.Items(items,8); if(lease is not Session session || session.Closed) throw new InvalidOperationException("Closed item owner.");
        using var c=Connect(); using var transaction=c.BeginTransaction(); using var fence=c.CreateCommand(); fence.Transaction=transaction;
        fence.CommandText="SELECT 1 FROM characters WHERE character_id=$id AND owner_id=$owner AND revision=$revision"; fence.Parameters.AddWithValue("$id",session.CharacterId.ToString()); fence.Parameters.AddWithValue("$owner",session.OwnerId.ToString()); fence.Parameters.AddWithValue("$revision",session.Revision);
        if(fence.ExecuteScalar() is null) throw new InvalidOperationException("Inventory ownership fence conflict.");
        foreach(var item in items) { token.ThrowIfCancellationRequested(); EnsureItemOwner(c,transaction,item,"c:"+session.CharacterId); }
        transaction.Commit();
    },token);
    private static void EnsureItemOwner(SqliteConnection c,SqliteTransaction transaction,Guid item,string owner)
    {
        using var q=c.CreateCommand(); q.Transaction=transaction; q.CommandText="INSERT INTO item_owners VALUES($item,$owner) ON CONFLICT(instance_id) DO NOTHING"; q.Parameters.AddWithValue("$item",item.ToString()); q.Parameters.AddWithValue("$owner",owner); q.ExecuteNonQuery();
        q.CommandText="SELECT owner_key FROM item_owners WHERE instance_id=$item"; if(q.ExecuteScalar() is not string actual || actual!=owner) throw new InvalidOperationException("Item already belongs to another owner.");
    }
    private static void SyncOwnership(SqliteConnection c,SqliteTransaction transaction,IReadOnlyList<DatabaseSave> changes,DatabaseWorldSave? world)
    {
        var sets=OwnershipValidation.Sets(changes,world); var prior=new HashSet<Guid>(); var retained=sets.Values.SelectMany(ids=>ids).ToHashSet();
        foreach(var owner in sets.Keys) { using var q=c.CreateCommand(); q.Transaction=transaction; q.CommandText="SELECT instance_id FROM item_owners WHERE owner_key=$owner"; q.Parameters.AddWithValue("$owner",owner); using var r=q.ExecuteReader(); while(r.Read()) prior.Add(Guid.Parse(r.GetString(0))); }
        foreach(var owner in sets.Keys.Order()) { using var q=c.CreateCommand(); q.Transaction=transaction; q.CommandText="DELETE FROM item_owners WHERE owner_key=$owner"; q.Parameters.AddWithValue("$owner",owner); q.ExecuteNonQuery(); }
        foreach(var change in changes) if(change.GroundClaims is { } claims) foreach(var item in claims)
        { using var q=c.CreateCommand(); q.Transaction=transaction; q.CommandText="DELETE FROM item_owners WHERE instance_id=$item AND owner_key=$owner"; q.Parameters.AddWithValue("$item",item.ToString()); q.Parameters.AddWithValue("$owner","g:"+item); q.ExecuteNonQuery(); }
        foreach(var removed in prior) if(!retained.Contains(removed)) EnsureItemOwner(c,transaction,removed,"d:"+removed);
        foreach(var (owner,items) in sets.OrderBy(pair=>pair.Key)) foreach(var item in items.Order())
        { using var q=c.CreateCommand(); q.Transaction=transaction; q.CommandText="INSERT INTO item_owners VALUES($item,$owner)"; q.Parameters.AddWithValue("$item",item.ToString()); q.Parameters.AddWithValue("$owner",owner); q.ExecuteNonQuery(); }
    }
}
