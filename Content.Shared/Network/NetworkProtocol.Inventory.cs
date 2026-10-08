using System.Text;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(InventoryCommand value)
    {
        if (value.Sequence == 0 || value.ItemHandle == 0 || !Enum.IsDefined(value.Action)) throw new ArgumentException("Invalid inventory intent.");
        var writer = CreateWriter(NetworkMessageType.InventoryCommand);
        writer.Put(value.Sequence); writer.Put((byte)value.Action); writer.Put(value.ItemHandle); return writer;
    }
    public static bool TryReadInventoryCommand(NetDataReader reader, out InventoryCommand value)
    {
        value = default;
        if (reader.AvailableBytes != 13 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetByte(out var action) || !Enum.IsDefined((InventoryAction)action) ||
            !reader.TryGetULong(out var handle) || handle == 0) return false;
        value = new(sequence, (InventoryAction)action, handle); return true;
    }
    public static NetDataWriter Write(InventoryResult value)
    {
        if (value.Sequence == 0 || !Enum.IsDefined(value.Outcome)) throw new ArgumentException("Invalid inventory result.");
        var writer = CreateWriter(NetworkMessageType.InventoryResult);
        writer.Put(value.Sequence); writer.Put(value.ServerTick); writer.Put((byte)value.Outcome); return writer;
    }
    public static bool TryReadInventoryResult(NetDataReader reader, out InventoryResult value)
    {
        value = default;
        if (reader.AvailableBytes != 9 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var outcome) || !Enum.IsDefined((InventoryOutcome)outcome)) return false;
        value = new(sequence, tick, (InventoryOutcome)outcome); return true;
    }
    public static NetDataWriter Write(InventoryState value)
    {
        if (!value.EntityId.IsValid || value.Items.Count > NetworkConstants.MaxInventoryItems) throw new ArgumentException("Invalid inventory state.");
        var writer = CreateWriter(NetworkMessageType.InventoryState);
        writer.Put(value.EntityId.Value); writer.Put(value.ServerTick); writer.Put((byte)value.Items.Count);
        var ids = new HashSet<ulong>(); var slots = new HashSet<EquipmentSlot>();
        foreach (var item in value.Items)
        {
            if (!ValidInventoryEntry(item) || !ids.Add(item.Handle) || (item.Equipped && !slots.Add(item.Slot)))
                throw new ArgumentException("Invalid inventory item.");
            writer.Put(item.Handle); writer.Put(item.Name); writer.Put((byte)item.Slot); writer.Put(item.Equipped);
            writer.Put(item.AttackBonus); writer.Put(item.DefenseBonus); writer.Put(item.HealthBonus);
        }
        return writer;
    }
    public static bool TryReadInventoryState(NetDataReader reader, out InventoryState value)
    {
        value = default;
        if (!reader.TryGetULong(out var entity) || entity == 0 || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetByte(out var count) || count > NetworkConstants.MaxInventoryItems) return false;
        var items = new InventoryEntry[count]; var ids = new HashSet<ulong>(); var slots = new HashSet<EquipmentSlot>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryGetULong(out var handle) || !reader.TryGetString(out var name) ||
                !reader.TryGetByte(out var slot) || !reader.TryGetByte(out var equipped) || equipped > 1 ||
                !reader.TryGetDouble(out var attack) || !reader.TryGetDouble(out var defense) || !reader.TryGetDouble(out var health)) return false;
            var item = new InventoryEntry(handle, name, (EquipmentSlot)slot, equipped == 1, attack, defense, health);
            if (!ValidInventoryEntry(item) || !ids.Add(handle) || (item.Equipped && !slots.Add(item.Slot))) return false;
            items[i] = item;
        }
        if (reader.AvailableBytes != 0) return false;
        value = new(new(entity), tick, items); return true;
    }
    private static bool ValidInventoryEntry(InventoryEntry item) => item.Handle != 0 &&
        item.Name is { Length: > 0 and <= 24 } && Encoding.UTF8.GetByteCount(item.Name) <= 48 &&
        Enum.IsDefined(item.Slot) && item.Slot != EquipmentSlot.None &&
        double.IsFinite(item.AttackBonus) && double.IsFinite(item.DefenseBonus) && double.IsFinite(item.HealthBonus);
}
