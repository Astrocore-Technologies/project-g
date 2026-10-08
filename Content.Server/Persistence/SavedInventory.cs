using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared.Network;

namespace Content.Server.Persistence;

public sealed record SavedItem(Guid InstanceId, string DefinitionId, EquipmentSlot EquippedSlot);
public sealed record SavedInventory
{
    [JsonRequired] public int Version { get; init; } = 1;
    public required SavedItem[] Items { get; init; }
    private static readonly JsonSerializerOptions Json = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, MaxDepth = 8 };
    public static SavedInventory Empty => new() { Items = [] };
    public void Validate()
    {
        if (Version != 1 || Items is null || Items.Length > NetworkConstants.MaxInventoryItems) throw new InvalidDataException("Invalid inventory model.");
        var ids = new HashSet<Guid>(); var slots = new HashSet<EquipmentSlot>();
        foreach (var item in Items)
            if (item is null || item.InstanceId == Guid.Empty || !ids.Add(item.InstanceId) ||
                string.IsNullOrWhiteSpace(item.DefinitionId) || item.DefinitionId.Length > 64 || !Enum.IsDefined(item.EquippedSlot) ||
                (item.EquippedSlot != EquipmentSlot.None && !slots.Add(item.EquippedSlot)))
                throw new InvalidDataException("Invalid inventory instance/slot.");
    }
    public string Serialize() { Validate(); return JsonSerializer.Serialize(this, Json); }
    public static SavedInventory Deserialize(string json)
    {
        if (json.Length > 8192) throw new InvalidDataException("Inventory model exceeds size budget.");
        var value = JsonSerializer.Deserialize<SavedInventory>(json, Json) ?? throw new InvalidDataException("Missing inventory.");
        value.Validate(); return value;
    }
}
