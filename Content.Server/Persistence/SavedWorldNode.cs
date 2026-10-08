using System.Text.Json;
using System.Text.Json.Serialization;
namespace Content.Server.Persistence;
public sealed record SavedWorldNode
{
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonRequired] public int Repairs { get; init; }
    [JsonRequired] public int Patrols { get; init; }
    [JsonRequired] public bool StormRumor { get; init; }
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=4 };
    public void Validate() { if (Version!=1 || Repairs is < 0 or > 64 || Patrols is < 0 or > 64) throw new InvalidDataException("Invalid saved world node."); }
    public string Serialize() { Validate(); return JsonSerializer.Serialize(this,Json); }
    public static SavedWorldNode Deserialize(string source)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(source)>8192) throw new InvalidDataException("World document too large.");
        using var d=JsonDocument.Parse(source,new JsonDocumentOptions { MaxDepth=4 });
        var names=new HashSet<string>(); foreach(var p in d.RootElement.EnumerateObject()) if(!names.Add(p.Name)) throw new InvalidDataException("Duplicate world property.");
        var state=JsonSerializer.Deserialize<SavedWorldNode>(source,Json) ?? throw new InvalidDataException("Missing world state."); state.Validate(); return state;
    }
}
public abstract class WorldNodeSession(long revision,SavedWorldNode state) : IAsyncDisposable
{
    public long Revision { get; internal set; }=revision;
    public SavedWorldNode State { get; }=state;
    public abstract ValueTask DisposeAsync();
}
public sealed record WorldNodeSave(WorldNodeSession Session,SavedWorldNode State,IReadOnlyList<Content.Database.DatabaseWorldAudit> Audit);
