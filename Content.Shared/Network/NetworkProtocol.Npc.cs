using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
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
