using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Respawn;

namespace Content.Server.Progression;

/// <summary>Temporary alpha price and explicitly selected settlement services, editable without code.</summary>
public sealed record RespecServiceDefinition(int Version,int Revision,int PaidCoins,float QuoteSeconds,string[] Taverns)
{
    public void Validate(TavernCatalog catalog)
    {
        if(Version!=1||Revision<1||PaidCoins is <1 or >1000000||!float.IsFinite(QuoteSeconds)||QuoteSeconds is <1 or >60||
            Taverns is not {Length:>0 and <=16}||Taverns.Distinct(StringComparer.Ordinal).Count()!=Taverns.Length||
            Taverns.Any(t=>t is null||!catalog.Taverns.ContainsKey(t)))
            throw new InvalidDataException("Invalid full reset service/price or tavern reference.");
    }
    public static RespecServiceDefinition LoadFile(string path)
    {
        var bytes=File.ReadAllBytes(path);if(bytes.Length>4096)throw new InvalidDataException("Oversized reset service.");
        return JsonSerializer.Deserialize<RespecServiceDefinition>(bytes,new JsonSerializerOptions
        {PropertyNameCaseInsensitive=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true,MaxDepth=3})
            ??throw new InvalidDataException("Missing reset service.");
    }
}
