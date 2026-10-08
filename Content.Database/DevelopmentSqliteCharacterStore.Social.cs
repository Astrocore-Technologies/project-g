using Microsoft.Data.Sqlite;
namespace Content.Database;
public sealed partial class DevelopmentSqliteCharacterStore
{
    public Task<IReadOnlyList<DatabaseSocialRow>> LoadSocialAsync(string realm,CancellationToken token)=>Task.Run<IReadOnlyList<DatabaseSocialRow>>(()=>
    {
        WorldStorageValidation.Key(realm);using var c=Connect();var rows=new Dictionary<string,DatabaseSocialRow>();
        using(var q=c.CreateCommand()){q.CommandText="SELECT record_key,kind,revision,state FROM social_records WHERE realm=$realm LIMIT 8193";q.Parameters.AddWithValue("$realm",realm);using var r=q.ExecuteReader();while(r.Read()){token.ThrowIfCancellationRequested();var row=new DatabaseSocialRow(r.GetString(0),(byte)r.GetInt32(1),r.GetInt64(2),r.GetString(3),"",[]);rows.Add(row.Key,row);}}
        using(var q=c.CreateCommand()){q.CommandText="SELECT kind,character_id,aggregate_key FROM social_members WHERE realm=$realm LIMIT 8193";q.Parameters.AddWithValue("$realm",realm);using var r=q.ExecuteReader();var count=0;while(r.Read()){if(++count>8192)throw new InvalidDataException("Membership budget exceeded.");var key=r.GetString(2);if(!rows.TryGetValue(key,out var row)||row.Kind!=r.GetInt32(0))throw new InvalidDataException("Orphan social member.");rows[key]=row with{Members=[..row.Members,Guid.Parse(r.GetString(1))]};}}
        using(var q=c.CreateCommand()){q.CommandText="SELECT name,aggregate_key FROM social_names WHERE realm=$realm LIMIT 33";q.Parameters.AddWithValue("$realm",realm);using var r=q.ExecuteReader();var count=0;while(r.Read()){if(++count>32||!rows.TryGetValue(r.GetString(1),out var row)||row.Kind!=3)throw new InvalidDataException("Invalid social name index.");rows[row.Key]=row with{Name=r.GetString(0)};}}
        var result=rows.Values.ToArray();SocialStorageValidation.Rows(result);return result;
    },token);
    private static void WriteSocial(SqliteConnection c,SqliteTransaction transaction,DatabaseSocialSave social,CancellationToken token)
    {
        SocialStorageValidation.Rows(social.Rows);WriteWorld(c,transaction,social.Realm);var realm=social.Realm.Session.Key;
        foreach(var row in social.Rows.Where(r=>r.Kind is 2 or 3)){using var q=c.CreateCommand();q.Transaction=transaction;q.CommandText="DELETE FROM social_members WHERE realm=$realm AND aggregate_key=$key;DELETE FROM social_names WHERE realm=$realm AND aggregate_key=$key";q.Parameters.AddWithValue("$realm",realm);q.Parameters.AddWithValue("$key",row.Key);q.ExecuteNonQuery();}
        foreach(var row in social.Rows)
        {
            token.ThrowIfCancellationRequested();using var q=c.CreateCommand();q.Transaction=transaction;q.CommandText="INSERT INTO social_records VALUES($realm,$key,$kind,$rev,$state) ON CONFLICT(realm,record_key) DO UPDATE SET revision=excluded.revision,state=excluded.state WHERE social_records.kind=excluded.kind AND social_records.revision<excluded.revision";q.Parameters.AddWithValue("$realm",realm);q.Parameters.AddWithValue("$key",row.Key);q.Parameters.AddWithValue("$kind",row.Kind);q.Parameters.AddWithValue("$rev",row.Revision);q.Parameters.AddWithValue("$state",row.State);if(q.ExecuteNonQuery()!=1)throw new InvalidOperationException("Social revision conflict.");
            foreach(var member in row.Members){using var m=c.CreateCommand();m.Transaction=transaction;m.CommandText="INSERT INTO social_members VALUES($realm,$kind,$id,$key)";m.Parameters.AddWithValue("$realm",realm);m.Parameters.AddWithValue("$kind",row.Kind);m.Parameters.AddWithValue("$id",member.ToString());m.Parameters.AddWithValue("$key",row.Key);m.ExecuteNonQuery();}
            if(row.Name.Length>0){using var n=c.CreateCommand();n.Transaction=transaction;n.CommandText="INSERT INTO social_names VALUES($realm,$name,$key)";n.Parameters.AddWithValue("$realm",realm);n.Parameters.AddWithValue("$name",row.Name);n.Parameters.AddWithValue("$key",row.Key);n.ExecuteNonQuery();}
        }
    }
}
