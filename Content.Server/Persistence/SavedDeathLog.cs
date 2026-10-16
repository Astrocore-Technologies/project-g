using Content.Shared.Combat;
namespace Content.Server.Persistence;

/// <summary>Immutable death evidence, stored separately from the bounded character document.</summary>
public sealed record SavedDeathLog(int Version,ulong DeathId,long AtUnixMilliseconds,CombatJournalEntry[] Entries)
{
    public void Validate()
    {
        if(Version!=1 || DeathId is 0 or >long.MaxValue || AtUnixMilliseconds is <0 or >253402300799999 ||
            Entries is null || Entries.Length>300)throw new InvalidDataException("Invalid death log header.");
        ulong previous=0;long previousTime=0;
        foreach(var entry in Entries)
        {
            if(!entry.IsValid || entry.Id<=previous || entry.AtUnixMilliseconds<previousTime ||
                entry.AtUnixMilliseconds>AtUnixMilliseconds || AtUnixMilliseconds-entry.AtUnixMilliseconds>30000)
                throw new InvalidDataException("Invalid death log entry/order/window.");
            previous=entry.Id;previousTime=entry.AtUnixMilliseconds;
        }
        if(Entries.Length==0 || Entries[^1].Kind!=CombatJournalKind.Death || Entries[^1].AtUnixMilliseconds!=AtUnixMilliseconds)
            throw new InvalidDataException("Death log requires its terminal death event.");
    }
}

public static class DeathLogDocuments
{
    private static readonly System.Text.Json.JsonSerializerOptions Json=new()
    {UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true,MaxDepth=8,
        Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    public static Content.Database.DatabaseDeathLogRow[] Serialize(IReadOnlyList<SavedDeathLog> logs)
    {
        if(logs.Count>10)throw new InvalidDataException("Too many death logs.");
        var rows=new Content.Database.DatabaseDeathLogRow[logs.Count];ulong previous=0;
        for(var i=0;i<logs.Count;i++)
        {
            var log=logs[i];log.Validate();if(log.DeathId<=previous)throw new InvalidDataException("Duplicate or unordered death logs.");previous=log.DeathId;
            var document=System.Text.Json.JsonSerializer.Serialize(log,Json);
            if(System.Text.Encoding.UTF8.GetByteCount(document)>262144)throw new InvalidDataException("Death log document exceeds budget.");
            rows[i]=new(log.DeathId,log.Version,document);
        }
        return rows;
    }
    public static SavedDeathLog[] Deserialize(IReadOnlyList<Content.Database.DatabaseDeathLogRow> rows)
    {
        if(rows.Count>10)throw new InvalidDataException("Too many death log documents.");
        var logs=new SavedDeathLog[rows.Count];ulong previous=0;
        for(var i=0;i<rows.Count;i++)
        {
            var row=rows[i];
            if(row.Version!=1 || row.DeathId<=previous || System.Text.Encoding.UTF8.GetByteCount(row.State)>262144)throw new InvalidDataException("Invalid death log version/order/size.");
            try
            {
                var log=System.Text.Json.JsonSerializer.Deserialize<SavedDeathLog>(row.State,Json)??throw new InvalidDataException("Missing death log.");
                log.Validate();if(log.DeathId!=row.DeathId || log.Version!=row.Version)throw new InvalidDataException("Death log identity mismatch.");
                logs[i]=log;previous=log.DeathId;
            }
            catch(System.Text.Json.JsonException error){throw new InvalidDataException("Invalid death log document.",error);}
        }
        return logs;
    }
    public static string SerializeSession(IReadOnlyList<CombatJournalEntry> entries)
    {
        ValidateSession(entries);var document=System.Text.Json.JsonSerializer.Serialize(entries,Json);
        if(System.Text.Encoding.UTF8.GetByteCount(document)>262144)throw new InvalidDataException("Session journal document exceeds budget.");return document;
    }
    public static CombatJournalEntry[] DeserializeSession(string document)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(document)>262144)throw new InvalidDataException("Session journal document exceeds budget.");
        try
        {var entries=System.Text.Json.JsonSerializer.Deserialize<CombatJournalEntry[]>(document,Json)??throw new InvalidDataException("Missing session journal.");ValidateSession(entries);return entries;}
        catch(System.Text.Json.JsonException error){throw new InvalidDataException("Invalid session journal.",error);}
    }
    private static void ValidateSession(IReadOnlyList<CombatJournalEntry> entries)
    {
        if(entries.Count>300)throw new InvalidDataException("Session journal exceeds D049.");ulong previous=0;long at=0;
        foreach(var entry in entries)
        {if(!entry.IsValid || entry.Id<=previous || entry.AtUnixMilliseconds<at)throw new InvalidDataException("Invalid session journal order.");previous=entry.Id;at=entry.AtUnixMilliseconds;}
    }
}
