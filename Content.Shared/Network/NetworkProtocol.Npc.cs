using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(NpcArea value)
    {
        if (!ValidNpcArea(value)) throw new ArgumentException("Invalid NPC area.");
        var writer = CreateWriter(NetworkMessageType.NpcArea);
        writer.Put(value.ActorId.Value); writer.Put(value.Sequence); writer.Put(value.ServerTick);
        WriteVector2(writer, value.Center); writer.Put(value.Radius); writer.Put(value.RemainingSeconds); writer.Put((byte)value.Phase);
        return writer;
    }
    public static bool TryReadNpcArea(NetDataReader reader, out NpcArea value)
    {
        value = default;
        if (reader.AvailableBytes != 33 || !reader.TryGetULong(out var id) || !reader.TryGetUInt(out var sequence) ||
            !reader.TryGetUInt(out var tick) || !TryReadVector2(reader, out var center) || !reader.TryGetFloat(out var radius) ||
            !reader.TryGetFloat(out var remaining) || !reader.TryGetByte(out var phase)) return false;
        var candidate = new NpcArea(new(id), sequence, tick, center, radius, remaining, (NpcAreaPhase)phase);
        if (!ValidNpcArea(candidate)) return false;
        value = candidate; return true;
    }
    private static bool ValidNpcArea(NpcArea value) => value.ActorId.IsValid && value.Sequence != 0 && Finite(value.Center) &&
        float.IsFinite(value.Radius) && value.Radius > 0 && float.IsFinite(value.RemainingSeconds) &&
        value.RemainingSeconds is >= 0 and <= 10 && Enum.IsDefined(value.Phase) &&
        (value.Phase != NpcAreaPhase.Finished || value.RemainingSeconds == 0);

    public static NetDataWriter Write(NpcWindup value)
    {
        if (!ValidWindup(value)) throw new ArgumentException("Invalid NPC telegraph.");
        var writer = CreateWriter(NetworkMessageType.NpcWindup);
        writer.Put(value.ActorId.Value); writer.Put(value.Sequence); writer.Put(value.ServerTick);
        WriteVector2(writer, value.Origin); WriteVector2(writer, value.Direction);
        writer.Put(value.Range); writer.Put(value.RemainingSeconds);
        return writer;
    }

    public static bool TryReadNpcWindup(NetDataReader reader, out NpcWindup value)
    {
        value = default;
        if (reader.AvailableBytes != 40 || !reader.TryGetULong(out var id) || !reader.TryGetUInt(out var sequence) ||
            !reader.TryGetUInt(out var tick) || !TryReadVector2(reader, out var origin) ||
            !TryReadVector2(reader, out var direction) || !reader.TryGetFloat(out var range) ||
            !reader.TryGetFloat(out var remaining)) return false;
        var candidate = new NpcWindup(new(id), sequence, tick, origin, direction, range, remaining);
        if (!ValidWindup(candidate)) return false;
        value = candidate; return true;
    }

    private static bool ValidWindup(NpcWindup value) => value.ActorId.IsValid && value.Sequence != 0 &&
        Finite(value.Origin) && BasicAttackShape.IsValidDirection(value.Direction) &&
        float.IsFinite(value.Range) && value.Range > 0 && float.IsFinite(value.RemainingSeconds) &&
        value.RemainingSeconds is >= 0 and <= 10;
}
