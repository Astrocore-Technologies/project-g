using Npgsql;
using NpgsqlTypes;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
namespace Content.Database;
public sealed partial class PostgresCharacterStore
{
    public async Task<DatabaseWorldSession> OpenWorldAsync(string key,string initial,CancellationToken token)
    {
        WorldStorageValidation.Key(key); StorageBounds.Document(initial);
        var c=await _source.OpenConnectionAsync(token); var lockKey=BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes("world:"+key))); var locked=false;
        try
        {
            await using var acquire=new NpgsqlCommand("SELECT pg_try_advisory_lock($1)",c); acquire.Parameters.AddWithValue(lockKey);
            locked=(bool)(await acquire.ExecuteScalarAsync(token))!; if (!locked) throw new InvalidOperationException("World is already owned.");
            await using var transaction=await c.BeginTransactionAsync(token); var owner=Guid.NewGuid();
            await using var seed=new NpgsqlCommand("INSERT INTO project_g_world_nodes VALUES ($1,$2,0,1,$3) ON CONFLICT(node_key) DO NOTHING",c,transaction);
            seed.Parameters.AddWithValue(key); seed.Parameters.AddWithValue(owner); seed.Parameters.AddWithValue(NpgsqlDbType.Jsonb,initial); await seed.ExecuteNonQueryAsync(token);
            await using var claim=new NpgsqlCommand("UPDATE project_g_world_nodes SET owner_id=$1,revision=revision+1 WHERE node_key=$2 AND revision < 9223372036854775807 RETURNING revision,model_version,state::text",c,transaction);
            claim.Parameters.AddWithValue(owner); claim.Parameters.AddWithValue(key); await using var r=await claim.ExecuteReaderAsync(token);
            if (!await r.ReadAsync(token) || r.GetInt32(1)!=1) throw new InvalidDataException("Unsupported world model.");
            var revision=r.GetInt64(0); var state=r.GetString(2); StorageBounds.Document(state); await r.DisposeAsync(); await transaction.CommitAsync(token);
            return new WorldSession(key,owner,revision,state,c,lockKey);
        }
        catch { if (locked) await UnlockAsync(c,lockKey); else await c.DisposeAsync(); throw; }
    }
    private static async Task WriteWorldAsync(NpgsqlConnection c,NpgsqlTransaction transaction,DatabaseWorldSave w,CancellationToken token)
    {
        WorldStorageValidation.Write(w);
        if (w.Session is not WorldSession session || session.Closed) throw new InvalidOperationException("Closed world owner.");
        await using var probe=new NpgsqlCommand("SELECT 1",session.Connection); await probe.ExecuteScalarAsync(token);
        await using var q=new NpgsqlCommand("UPDATE project_g_world_nodes SET state=$1,revision=revision+1 WHERE node_key=$2 AND owner_id=$3 AND revision=$4 AND model_version=1",c,transaction);
        q.Parameters.AddWithValue(NpgsqlDbType.Jsonb,w.State); q.Parameters.AddWithValue(session.Key); q.Parameters.AddWithValue(session.OwnerId); q.Parameters.AddWithValue(w.ExpectedRevision);
        if (await q.ExecuteNonQueryAsync(token)!=1) throw new InvalidOperationException("World ownership/revision conflict.");
        for (var i=0;i<w.Audit.Count;i++)
        {
            var a=w.Audit[i]; await using var audit=new NpgsqlCommand("INSERT INTO project_g_world_audit VALUES ($1,$2,$3,$4,$5,$6,$7)",c,transaction);
            audit.Parameters.AddWithValue(session.Key); audit.Parameters.AddWithValue(w.ExpectedRevision+1); audit.Parameters.AddWithValue(i); audit.Parameters.AddWithValue(a.Actor); audit.Parameters.AddWithValue(a.Operation); audit.Parameters.AddWithValue(a.Reason); audit.Parameters.AddWithValue(a.Timestamp); await audit.ExecuteNonQueryAsync(token);
        }
    }
    private sealed class WorldSession(string key,Guid owner,long revision,string state,NpgsqlConnection c,long lockKey) : DatabaseWorldSession(key,owner,revision,state)
    {
        internal NpgsqlConnection Connection { get; }=c;
        internal bool Closed { get; private set; }
        public override ValueTask DisposeAsync() { Closed=true; return UnlockAsync(Connection,lockKey); }
    }
}
