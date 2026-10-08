using Npgsql;
using NpgsqlTypes;
namespace Content.Database;
public sealed partial class PostgresCharacterStore
{
    public async Task<IReadOnlyList<DatabaseSocialRow>> LoadSocialAsync(string realm,CancellationToken token)
    {
        WorldStorageValidation.Key(realm);await using var c=await _source.OpenConnectionAsync(token);var rows=new Dictionary<string,DatabaseSocialRow>();
        await using(var q=new NpgsqlCommand("SELECT record_key,kind,revision,state::text FROM social_records WHERE realm=$1 LIMIT 8193",c)){q.Parameters.AddWithValue(realm);await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token)){var row=new DatabaseSocialRow(r.GetString(0),(byte)r.GetInt32(1),r.GetInt64(2),r.GetString(3),"",[]);rows.Add(row.Key,row);}}
        await using(var q=new NpgsqlCommand("SELECT kind,character_id,aggregate_key FROM social_members WHERE realm=$1 LIMIT 8193",c)){q.Parameters.AddWithValue(realm);await using var r=await q.ExecuteReaderAsync(token);var count=0;while(await r.ReadAsync(token)){if(++count>8192)throw new InvalidDataException("Membership budget exceeded.");var key=r.GetString(2);if(!rows.TryGetValue(key,out var row)||row.Kind!=r.GetInt32(0))throw new InvalidDataException("Orphan social member.");rows[key]=row with{Members=[..row.Members,r.GetGuid(1)]};}}
        await using(var q=new NpgsqlCommand("SELECT name,aggregate_key FROM social_names WHERE realm=$1 LIMIT 33",c)){q.Parameters.AddWithValue(realm);await using var r=await q.ExecuteReaderAsync(token);var count=0;while(await r.ReadAsync(token)){if(++count>32||!rows.TryGetValue(r.GetString(1),out var row)||row.Kind!=3)throw new InvalidDataException("Invalid social name index.");rows[row.Key]=row with{Name=r.GetString(0)};}}
        var result=rows.Values.ToArray();SocialStorageValidation.Rows(result);return result;
    }
    private static async Task WriteSocialAsync(NpgsqlConnection c,NpgsqlTransaction transaction,DatabaseSocialSave social,CancellationToken token)
    {
        SocialStorageValidation.Rows(social.Rows);await WriteWorldAsync(c,transaction,social.Realm,token);var realm=social.Realm.Session.Key;
        foreach(var row in social.Rows.Where(r=>r.Kind is 2 or 3)){await using var q=new NpgsqlCommand("DELETE FROM social_members WHERE realm=$1 AND aggregate_key=$2",c,transaction);q.Parameters.AddWithValue(realm);q.Parameters.AddWithValue(row.Key);await q.ExecuteNonQueryAsync(token);q.CommandText="DELETE FROM social_names WHERE realm=$1 AND aggregate_key=$2";await q.ExecuteNonQueryAsync(token);}
        foreach(var row in social.Rows)
        {
            await using var q=new NpgsqlCommand("INSERT INTO social_records VALUES($1,$2,$3,$4,$5) ON CONFLICT(realm,record_key) DO UPDATE SET revision=excluded.revision,state=excluded.state WHERE social_records.kind=excluded.kind AND social_records.revision<excluded.revision",c,transaction);q.Parameters.AddWithValue(realm);q.Parameters.AddWithValue(row.Key);q.Parameters.AddWithValue((int)row.Kind);q.Parameters.AddWithValue(row.Revision);q.Parameters.AddWithValue(NpgsqlDbType.Jsonb,row.State);if(await q.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("Social revision conflict.");
            foreach(var member in row.Members){await using var m=new NpgsqlCommand("INSERT INTO social_members VALUES($1,$2,$3,$4)",c,transaction);m.Parameters.AddWithValue(realm);m.Parameters.AddWithValue((int)row.Kind);m.Parameters.AddWithValue(member);m.Parameters.AddWithValue(row.Key);await m.ExecuteNonQueryAsync(token);}
            if(row.Name.Length>0){await using var n=new NpgsqlCommand("INSERT INTO social_names VALUES($1,$2,$3)",c,transaction);n.Parameters.AddWithValue(realm);n.Parameters.AddWithValue(row.Name);n.Parameters.AddWithValue(row.Key);await n.ExecuteNonQueryAsync(token);}
        }
    }
}
