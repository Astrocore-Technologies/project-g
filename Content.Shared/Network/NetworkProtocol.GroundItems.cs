using System.Numerics;
using System.Text;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(PickupCommand value)
    {
        if (value.Sequence == 0 || value.Handle == 0) throw new ArgumentException("Invalid pickup intent.");
        var writer = CreateWriter(NetworkMessageType.PickupCommand);
        writer.Put(value.Sequence); writer.Put(value.Handle); return writer;
    }
    public static bool TryReadPickupCommand(NetDataReader reader, out PickupCommand value)
    {
        value = default;
        if (reader.AvailableBytes != 12 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetULong(out var handle) || handle == 0) return false;
        value = new(sequence, handle); return true;
    }
    public static NetDataWriter Write(PickupResult value)
    {
        if (value.Sequence == 0 || !Enum.IsDefined(value.Outcome)) throw new ArgumentException("Invalid pickup result.");
        var writer = CreateWriter(NetworkMessageType.PickupResult);
        writer.Put(value.Sequence); writer.Put(value.ServerTick); writer.Put((byte)value.Outcome); return writer;
    }
    public static bool TryReadPickupResult(NetDataReader reader, out PickupResult value)
    {
        value = default;
        if (reader.AvailableBytes != 9 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var outcome) || !Enum.IsDefined((PickupOutcome)outcome)) return false;
        value = new(sequence, tick, (PickupOutcome)outcome); return true;
    }
    public static NetDataWriter Write(GroundItemSpawn value)
    {
        if (value.Handle == 0 || !float.IsFinite(value.Position.X) || !float.IsFinite(value.Position.Y) ||
            value.Name is not { Length: > 0 and <= 24 } || Encoding.UTF8.GetByteCount(value.Name) > 48 ||
            value.Slot is not (EquipmentSlot.Weapon or EquipmentSlot.Armor)) throw new ArgumentException("Invalid ground item.");
        var writer = CreateWriter(NetworkMessageType.GroundItemSpawn);
        writer.Put(value.Handle); writer.Put(value.ServerTick); writer.Put(value.Position.X); writer.Put(value.Position.Y);
        writer.Put(value.Name); writer.Put((byte)value.Slot); WriteHeight(writer, value.Height); return writer;
    }
    public static bool TryReadGroundItemSpawn(NetDataReader reader, out GroundItemSpawn value)
    {
        value = default;
        if (!reader.TryGetULong(out var handle) || handle == 0 || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetFloat(out var x) || !reader.TryGetFloat(out var z) || !float.IsFinite(x) || !float.IsFinite(z) ||
            !reader.TryGetString(out var name) || name is not { Length: > 0 and <= 24 } || Encoding.UTF8.GetByteCount(name) > 48 ||
            !reader.TryGetByte(out var slot) || (EquipmentSlot)slot is not (EquipmentSlot.Weapon or EquipmentSlot.Armor) || !ReadHeight(reader, out var height) || reader.AvailableBytes != 0) return false;
        value = new(handle, tick, new Vector2(x, z), name, (EquipmentSlot)slot, height); return true;
    }
    public static NetDataWriter Write(GroundItemDespawn value)
    {
        if (value.Handle == 0) throw new ArgumentException("Invalid ground handle.");
        var writer = CreateWriter(NetworkMessageType.GroundItemDespawn);
        writer.Put(value.Handle); writer.Put(value.ServerTick); return writer;
    }
    public static bool TryReadGroundItemDespawn(NetDataReader reader, out GroundItemDespawn value)
    {
        value = default;
        if (reader.AvailableBytes != 12 || !reader.TryGetULong(out var handle) || handle == 0 || !reader.TryGetUInt(out var tick)) return false;
        value = new(handle, tick); return true;
    }
}
