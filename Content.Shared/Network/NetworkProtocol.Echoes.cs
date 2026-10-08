using System.Numerics;
using System.Text;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    private static bool EchoSlotValid(byte slot) => slot is >= 1 and <= NetworkConstants.MaxActiveEchoes;
    private static bool EchoPositionValid(Vector2 position) => float.IsFinite(position.X) && float.IsFinite(position.Y);
    private static bool EchoDurationValid(float value) => float.IsFinite(value) && value is >= 0 and <= 86400;
    public static NetDataWriter Write(EchoSpawn value)
    {
        if (!value.EntityId.IsValid || !value.OwnerId.IsValid || value.EntityId == value.OwnerId || !EchoSlotValid(value.Slot) ||
            !EchoPositionValid(value.Position) || value.Name is not { Length: > 0 and <= 24 } || Encoding.UTF8.GetByteCount(value.Name) > 48)
            throw new ArgumentException("Invalid Echo spawn.");
        var writer = CreateWriter(NetworkMessageType.EchoSpawn);
        writer.Put(value.EntityId.Value); writer.Put(value.OwnerId.Value); writer.Put(value.Slot); writer.Put(value.ServerTick);
        writer.Put(value.Position.X); writer.Put(value.Position.Y); writer.Put(value.Name); return writer;
    }
    public static bool TryReadEchoSpawn(NetDataReader reader, out EchoSpawn value)
    {
        value = default;
        if (!reader.TryGetULong(out var id) || id == 0 || !reader.TryGetULong(out var owner) || owner == 0 || id == owner ||
            !reader.TryGetByte(out var slot) || !EchoSlotValid(slot) || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetFloat(out var x) || !reader.TryGetFloat(out var z) || !EchoPositionValid(new(x,z)) ||
            !reader.TryGetString(out var name) || name is not { Length: > 0 and <= 24 } || Encoding.UTF8.GetByteCount(name) > 48 || reader.AvailableBytes != 0) return false;
        value = new(new(id), new(owner), slot, tick, new(x,z), name); return true;
    }
    public static NetDataWriter Write(EchoSignatureCommand value)
    {
        if (value.Sequence == 0 || !EchoSlotValid(value.Slot) || !EchoPositionValid(value.Aim)) throw new ArgumentException("Invalid Echo command.");
        var writer = CreateWriter(NetworkMessageType.EchoSignatureCommand);
        writer.Put(value.Sequence); writer.Put(value.ClientTick); writer.Put(value.Slot); writer.Put(value.Aim.X); writer.Put(value.Aim.Y); return writer;
    }
    public static bool TryReadEchoSignatureCommand(NetDataReader reader, out EchoSignatureCommand value)
    {
        value = default;
        if (reader.AvailableBytes != 17 || !reader.TryGetUInt(out var sequence) || sequence == 0 || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetByte(out var slot) || !EchoSlotValid(slot) || !reader.TryGetFloat(out var x) || !reader.TryGetFloat(out var z) || !EchoPositionValid(new(x,z))) return false;
        value = new(sequence,tick,slot,new(x,z)); return true;
    }
    public static NetDataWriter Write(EchoSignatureResult value)
    {
        if (value.Sequence == 0 || !EchoSlotValid(value.Slot) || !Enum.IsDefined(value.Outcome) || !EchoDurationValid(value.CooldownSeconds)) throw new ArgumentException("Invalid Echo result.");
        var writer = CreateWriter(NetworkMessageType.EchoSignatureResult);
        writer.Put(value.Sequence); writer.Put(value.ServerTick); writer.Put(value.Slot); writer.Put((byte)value.Outcome); writer.Put(value.CooldownSeconds); return writer;
    }
    public static bool TryReadEchoSignatureResult(NetDataReader reader, out EchoSignatureResult value)
    {
        value = default;
        if (reader.AvailableBytes != 14 || !reader.TryGetUInt(out var seq) || seq == 0 || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetByte(out var slot) || !EchoSlotValid(slot) || !reader.TryGetByte(out var outcome) || !Enum.IsDefined((EchoCommandOutcome)outcome) ||
            !reader.TryGetFloat(out var cooldown) || !EchoDurationValid(cooldown)) return false;
        value = new(seq,tick,slot,(EchoCommandOutcome)outcome,cooldown); return true;
    }
    public static NetDataWriter Write(EchoAction value)
    {
        if (!value.EntityId.IsValid || !Enum.IsDefined(value.Kind) || !EchoPositionValid(value.Position) || !float.IsFinite(value.Radius) || value.Radius is <= 0 or > 10 ||
            !double.IsFinite(value.Damage) || value.Damage < 0 || !double.IsFinite(value.TargetHealth) || value.TargetHealth < 0 ||
            (!value.TargetId.IsValid && (value.Damage != 0 || value.TargetHealth != 0))) throw new ArgumentException("Invalid Echo action.");
        var writer = CreateWriter(NetworkMessageType.EchoAction);
        writer.Put(value.EntityId.Value); writer.Put(value.ServerTick); writer.Put((byte)value.Kind); writer.Put(value.TargetId.Value);
        writer.Put(value.Position.X); writer.Put(value.Position.Y); writer.Put(value.Radius); writer.Put(value.Damage); writer.Put(value.TargetHealth); return writer;
    }
    public static bool TryReadEchoAction(NetDataReader reader, out EchoAction value)
    {
        value = default;
        if (reader.AvailableBytes != 49 || !reader.TryGetULong(out var id) || id == 0 || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetByte(out var kind) || !Enum.IsDefined((EchoActionKind)kind) || !reader.TryGetULong(out var target) ||
            !reader.TryGetFloat(out var x) || !reader.TryGetFloat(out var z) || !EchoPositionValid(new(x,z)) ||
            !reader.TryGetFloat(out var radius) || !float.IsFinite(radius) || radius is <= 0 or > 10 ||
            !reader.TryGetDouble(out var damage) || !double.IsFinite(damage) || damage < 0 ||
            !reader.TryGetDouble(out var health) || !double.IsFinite(health) || health < 0 || (target == 0 && (damage != 0 || health != 0))) return false;
        value = new(new(id),tick,(EchoActionKind)kind,new(target),new(x,z),radius,damage,health); return true;
    }
    public static NetDataWriter Write(EchoLoadout value)
    {
        if (!value.OwnerId.IsValid || value.Slots.Count > NetworkConstants.MaxActiveEchoes) throw new ArgumentException("Invalid Echo loadout.");
        var writer = CreateWriter(NetworkMessageType.EchoLoadout); writer.Put(value.OwnerId.Value); writer.Put(value.ServerTick); writer.Put((byte)value.Slots.Count);
        var slots = new HashSet<byte>(); var ids = new HashSet<NetworkEntityId>();
        foreach (var slot in value.Slots)
        {
            if (!ValidEchoSlot(slot) || !slots.Add(slot.Slot) || !ids.Add(slot.EntityId) || slot.EntityId == value.OwnerId) throw new ArgumentException("Invalid Echo slot.");
            writer.Put(slot.Slot); writer.Put(slot.EntityId.Value); writer.Put(slot.Range); writer.Put(slot.Radius); writer.Put(slot.CooldownSeconds);
        }
        return writer;
    }
    private static bool ValidEchoSlot(EchoSlot slot) => EchoSlotValid(slot.Slot) && slot.EntityId.IsValid &&
        float.IsFinite(slot.Range) && slot.Range is > 0 and <= 10 && float.IsFinite(slot.Radius) && slot.Radius is > 0 and <= 10 && EchoDurationValid(slot.CooldownSeconds);
    public static bool TryReadEchoLoadout(NetDataReader reader, out EchoLoadout value)
    {
        value = default;
        if (!reader.TryGetULong(out var owner) || owner == 0 || !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var count) || count > NetworkConstants.MaxActiveEchoes || reader.AvailableBytes != count * 21) return false;
        var entries = new EchoSlot[count]; var slots = new HashSet<byte>(); var ids = new HashSet<NetworkEntityId>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryGetByte(out var slot) || !reader.TryGetULong(out var id) || !reader.TryGetFloat(out var range) || !reader.TryGetFloat(out var radius) || !reader.TryGetFloat(out var cooldown)) return false;
            var entry = new EchoSlot(slot,new(id),range,radius,cooldown);
            if (!ValidEchoSlot(entry) || id == owner || !slots.Add(slot) || !ids.Add(entry.EntityId)) return false;
            entries[i] = entry;
        }
        value = new(new(owner),tick,entries); return true;
    }
}
