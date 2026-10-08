using System.Text.Json;
using System.Text.Json.Serialization;
namespace Content.Server.Persistence;
public sealed record SavedWorldNode
{
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SavedResourceStock[]? Resources { get; init; }
    [JsonRequired] public int Repairs { get; init; }
    [JsonRequired] public int Patrols { get; init; }
    [JsonRequired] public bool StormRumor { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SavedMarket? Market {get;init;}
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=8 };
    public void Validate() { if (Version!=1 || Repairs is < 0 or > 64 || Patrols is < 0 or > 64) throw new InvalidDataException("Invalid saved world node.");
        Market?.Validate();
        if(Resources is not null) { if(Resources.Length>16) throw new InvalidDataException("Resource budget exceeded."); var ids=new HashSet<ushort>(); foreach(var n in Resources) if(n is null || n.Id==0 || !ids.Add(n.Id) || n.Remaining is < 0 or > 1000 || n.RefillAt is <0 or >253402300799L || n.Remaining>0 && n.RefillAt!=0) throw new InvalidDataException("Invalid resource stock."); }
    }
    public string Serialize() { Validate(); return JsonSerializer.Serialize(this,Json); }
    public static SavedWorldNode Deserialize(string source)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(source)>8192) throw new InvalidDataException("World document too large.");
        using var d=JsonDocument.Parse(source,new JsonDocumentOptions { MaxDepth=8 });
        RejectDuplicates(d.RootElement);
        if(d.RootElement.TryGetProperty("Market",out var market) && market.ValueKind==JsonValueKind.Null)throw new InvalidDataException("Null market component.");
        if(d.RootElement.TryGetProperty("Resources",out var resources) && resources.ValueKind==JsonValueKind.Null) throw new InvalidDataException("Null resource component.");
        var state=JsonSerializer.Deserialize<SavedWorldNode>(source,Json) ?? throw new InvalidDataException("Missing world state."); state.Validate(); return state;
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object) { var names=new HashSet<string>(); foreach(var property in value.EnumerateObject()) { if(!names.Add(property.Name)) throw new InvalidDataException("Duplicate world property."); RejectDuplicates(property.Value); } }
        else if(value.ValueKind==JsonValueKind.Array) foreach(var child in value.EnumerateArray()) RejectDuplicates(child);
    }

}
public abstract class WorldNodeSession(long revision,SavedWorldNode state) : IAsyncDisposable
{
    public long Revision { get; internal set; }=revision;
    public SavedWorldNode State { get; }=state;
    public abstract ValueTask DisposeAsync();
}
public sealed record WorldNodeSave(WorldNodeSession Session,SavedWorldNode State,IReadOnlyList<Content.Database.DatabaseWorldAudit> Audit);
