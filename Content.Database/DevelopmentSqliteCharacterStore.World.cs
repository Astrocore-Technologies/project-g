using Microsoft.Data.Sqlite;
namespace Content.Database;
public sealed partial class DevelopmentSqliteCharacterStore
{
    public Task<DatabaseWorldSession> OpenWorldAsync(string key, string initial, CancellationToken token) => Task.Run<DatabaseWorldSession>(() =>
    {
        WorldStorageValidation.Key(key); StorageBounds.Document(initial);
        var held = new FileStream(_databasePath + ".world-" + key + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            token.ThrowIfCancellationRequested();
            using var c = Connect(); using var transaction = c.BeginTransaction(); using var q = c.CreateCommand(); q.Transaction = transaction;
            var owner = Guid.NewGuid(); q.CommandText = "INSERT INTO world_nodes VALUES ($key,$owner,0,1,$state) ON CONFLICT(node_key) DO NOTHING";
            q.Parameters.AddWithValue("$key",key); q.Parameters.AddWithValue("$owner",owner.ToString()); q.Parameters.AddWithValue("$state",initial); q.ExecuteNonQuery();
            q.CommandText = "UPDATE world_nodes SET owner_id=$owner,revision=revision+1 WHERE node_key=$key AND revision < 9223372036854775807 RETURNING revision,model_version,state";
            using var r = q.ExecuteReader();
            if (!r.Read() || r.GetInt32(1) != 1) throw new InvalidDataException("Unsupported world model.");
            var revision = r.GetInt64(0); var state = r.GetString(2); StorageBounds.Document(state); r.Dispose(); token.ThrowIfCancellationRequested(); transaction.Commit();
            return new WorldSession(key, owner, revision, state, held);
        }
        catch { held.Dispose(); throw; }
    },token);
    private static void WriteWorld(SqliteConnection c, SqliteTransaction transaction, DatabaseWorldSave w)
    {
        WorldStorageValidation.Write(w);
        if (w.Session is not WorldSession session || session.Closed) throw new InvalidOperationException("Closed world owner.");
        using var q = c.CreateCommand(); q.Transaction = transaction;
        q.CommandText = "UPDATE world_nodes SET state=$state,revision=revision+1 WHERE node_key=$key AND owner_id=$owner AND revision=$revision AND model_version=1";
        q.Parameters.AddWithValue("$state",w.State); q.Parameters.AddWithValue("$key",session.Key); q.Parameters.AddWithValue("$owner",session.OwnerId.ToString()); q.Parameters.AddWithValue("$revision",w.ExpectedRevision);
        if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("World ownership/revision conflict.");
        for (var i=0;i<w.Audit.Count;i++)
        {
            var a=w.Audit[i]; using var audit=c.CreateCommand(); audit.Transaction=transaction;
            audit.CommandText="INSERT INTO world_audit VALUES ($key,$revision,$ordinal,$actor,$operation,$reason,$time)";
            audit.Parameters.AddWithValue("$key",session.Key); audit.Parameters.AddWithValue("$revision",w.ExpectedRevision+1); audit.Parameters.AddWithValue("$ordinal",i);
            audit.Parameters.AddWithValue("$actor",a.Actor); audit.Parameters.AddWithValue("$operation",a.Operation); audit.Parameters.AddWithValue("$reason",a.Reason); audit.Parameters.AddWithValue("$time",a.Timestamp); audit.ExecuteNonQuery();
        }
    }
    private sealed class WorldSession(string key,Guid owner,long revision,string state,FileStream held) : DatabaseWorldSession(key,owner,revision,state)
    {
        internal bool Closed { get; private set; }
        public override ValueTask DisposeAsync() { Closed=true; held.Dispose(); return ValueTask.CompletedTask; }
    }
}
