using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared.Network;

namespace Content.Server.Persistence;

public sealed record SavedItem(Guid InstanceId, string DefinitionId, EquipmentSlot EquippedSlot)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] public bool Bound {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SavedItemCondition? Condition { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] public byte Enhancement {get;init;}
}
public sealed record SavedInventory
{
    [JsonRequired] public int Version { get; init; } = 1;
    public required SavedItem[] Items { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SavedCrafting? Crafting { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SavedMaintenance? Maintenance { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SavedEconomy? Economy {get;init;}
    private static readonly JsonSerializerOptions Json = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, MaxDepth = 8 };
    public static SavedInventory Empty => new() { Items = [] };
    public void Validate()
    {
        if (Version != 1 || Items is null || Items.Length > NetworkConstants.MaxInventoryItems) throw new InvalidDataException("Invalid inventory model.");
        Crafting?.Validate(); Maintenance?.Validate(); Economy?.Validate();
        var ids = new HashSet<Guid>(); var slots = new HashSet<EquipmentSlot>();
        foreach (var item in Items)
        {
            item?.Condition?.Validate();
            if(item is not null && (item.Enhancement>5 || item.Enhancement>0 && item.Condition is null))throw new InvalidDataException("Invalid enhancement.");
            if (item is null || item.InstanceId == Guid.Empty || !ids.Add(item.InstanceId) ||
                string.IsNullOrWhiteSpace(item.DefinitionId) || item.DefinitionId.Length > 64 || !Enum.IsDefined(item.EquippedSlot) ||
                (item.EquippedSlot != EquipmentSlot.None && !slots.Add(item.EquippedSlot)))
                throw new InvalidDataException("Invalid inventory instance/slot.");
        }
    }
    public string Serialize() { Validate(); return JsonSerializer.Serialize(this, Json); }
    public static SavedInventory Deserialize(string json)
    {
        if (json.Length > 8192) throw new InvalidDataException("Inventory model exceeds size budget.");
        using var document=JsonDocument.Parse(json);
        RejectDuplicates(document.RootElement);
        if(document.RootElement.TryGetProperty("Economy",out var economy) && economy.ValueKind==JsonValueKind.Null)throw new InvalidDataException("Null economy component.");
        if(document.RootElement.TryGetProperty("Maintenance",out var maintenance) && maintenance.ValueKind==JsonValueKind.Null) throw new InvalidDataException("Null maintenance component.");
        if(document.RootElement.TryGetProperty("Items",out var items) && items.ValueKind==JsonValueKind.Array) foreach(var item in items.EnumerateArray()) if(item.ValueKind==JsonValueKind.Object && item.TryGetProperty("Condition",out var condition) && condition.ValueKind==JsonValueKind.Null) throw new InvalidDataException("Null condition component.");
        if(document.RootElement.TryGetProperty("Crafting",out var crafting) && crafting.ValueKind==JsonValueKind.Null) throw new InvalidDataException("Null crafting component.");
        var value = JsonSerializer.Deserialize<SavedInventory>(json, Json) ?? throw new InvalidDataException("Missing inventory.");
        value.Validate(); return value;
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object) { var names=new HashSet<string>(); foreach(var property in value.EnumerateObject()) { if(!names.Add(property.Name)) throw new InvalidDataException("Duplicate inventory property."); RejectDuplicates(property.Value); } }
        else if(value.ValueKind==JsonValueKind.Array) foreach(var child in value.EnumerateArray()) RejectDuplicates(child);
    }

}
