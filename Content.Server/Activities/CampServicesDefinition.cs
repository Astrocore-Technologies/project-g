using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Data;

namespace Content.Server.Activities;

public sealed record ServicePoint(float X,float Height,float Z)
{
    public Vector3 Position=>new(X,Height,Z);
    public Vector2 Ground=>new(X,Z);
    internal bool Valid=>float.IsFinite(X)&&float.IsFinite(Height)&&float.IsFinite(Z)&&MathF.Abs(Height)<=128;
}
public sealed record ResourcePlacement(ushort Id,ServicePoint Point);
/// <summary>Editable camp placement and one explicitly approved alpha delivery. Balance remains server content.</summary>
public sealed record CampServicesDefinition(int Version,int Revision,string RegionId,ServicePoint Station,
    ResourcePlacement[] Resources,ServicePoint Giver,ServicePoint Recipient,string QuestObjective,string QuestOffer,string QuestReminder,
    string DeliveryId,string DeliveryTitle,string DeliveryObjective,ushort MaterialId,ushort Quantity,int Experience)
{
    public void Validate(ContentCatalog catalog)
    {
        Content.Server.Regions.RegionOwnership.ValidateRegion(RegionId);
        static bool Text(string value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&!value.Any(char.IsControl);
        if(Version!=1||Revision<1||Station?.Valid!=true||Giver?.Valid!=true||Recipient?.Valid!=true||
            Resources is not {Length:>0 and <=16}||Resources.Any(r=>r is null||r.Point?.Valid!=true)||
            Resources.Select(r=>r.Id).Distinct().Count()!=Resources.Length||catalog.Crafting is not {} crafting||catalog.DeliveryQuest is null||
            !Resources.Select(r=>r.Id).Order().SequenceEqual(crafting.Resources.Select(r=>r.Id).Order())||
            !Text(QuestObjective,100)||!Text(QuestOffer,250)||!Text(QuestReminder,250)||!Text(DeliveryId,64)||!Text(DeliveryTitle,64)||
            !Text(DeliveryObjective,180)||Quantity is 0 or >999||Experience is <1 or >1000000||!crafting.Materials.Any(m=>m.Id==MaterialId))
            throw new InvalidDataException("Invalid camp services/delivery definition.");
    }
    public static CampServicesDefinition LoadFile(string path)
    {
        var bytes=File.ReadAllBytes(path);if(bytes.Length>16384)throw new InvalidDataException("Oversized camp services.");
        return JsonSerializer.Deserialize<CampServicesDefinition>(bytes,new JsonSerializerOptions
        {PropertyNameCaseInsensitive=true,RespectRequiredConstructorParameters=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=5})
            ??throw new InvalidDataException("Missing camp services.");
    }
}
