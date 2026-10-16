using Microsoft.Data.Sqlite;
namespace Content.Database;

public sealed partial class DevelopmentSqliteCharacterStore
{
    private static IReadOnlyList<DatabaseDeathLogRow> ReadDeathLogs(SqliteConnection connection,SqliteTransaction transaction,Guid character)
    {
        using var query=connection.CreateCommand();query.Transaction=transaction;
        query.CommandText="SELECT death_id,model_version,state FROM character_death_logs WHERE character_id=$id ORDER BY death_id";
        query.Parameters.AddWithValue("$id",character.ToString());using var reader=query.ExecuteReader();var rows=new List<DatabaseDeathLogRow>();
        while(reader.Read())
        {if(rows.Count==10)throw new InvalidDataException("Too many saved death logs.");rows.Add(new(checked((ulong)reader.GetInt64(0)),reader.GetInt32(1),reader.GetString(2)));}
        DeathLogStorageValidation.Rows(rows);return rows.AsReadOnly();
    }
    private static void WriteDeathLogs(SqliteConnection connection,SqliteTransaction transaction,Guid character,
        IReadOnlyList<DatabaseDeathLogRow> rows,CancellationToken token)
    {
        DeathLogStorageValidation.Rows(rows);using var query=connection.CreateCommand();query.Transaction=transaction;
        query.CommandText="DELETE FROM character_death_logs WHERE character_id=$id";query.Parameters.AddWithValue("$id",character.ToString());query.ExecuteNonQuery();
        query.CommandText="INSERT INTO character_death_logs VALUES($id,$death,$version,$state)";
        var death=query.Parameters.Add("$death",SqliteType.Integer);var version=query.Parameters.Add("$version",SqliteType.Integer);
        var state=query.Parameters.Add("$state",SqliteType.Text);query.Prepare();
        foreach(var row in rows){token.ThrowIfCancellationRequested();death.Value=checked((long)row.DeathId);version.Value=row.Version;state.Value=row.State;query.ExecuteNonQuery();}
    }
}
