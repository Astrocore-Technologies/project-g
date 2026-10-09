using System.Numerics;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

// Public boundary geometry only; no destination content or unexplored map is sent.
public readonly record struct RegionGate(Vector2 Exit, float Radius);
public readonly record struct RegionEnter(ulong Epoch, string Region, Vector2 Exit, float Radius, ulong GeometryHash = 0,
    RegionGate[]? AdditionalGates = null);

public static partial class NetworkProtocol
{
    public const int RegionEnvelopeBytes = sizeof(ushort) + sizeof(ulong);

    private static bool ValidRegion(RegionEnter value) => value.Epoch > 0 &&
        value.Region is { Length: > 0 and <= 64 } &&
        value.Region.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') &&
        float.IsFinite(value.Exit.X) && float.IsFinite(value.Exit.Y) &&
        float.IsFinite(value.Radius) && value.Radius is > 0 and <= 3 &&
        (value.AdditionalGates is null || value.AdditionalGates.Length < NetworkConstants.MaxRegionGates &&
            value.AdditionalGates.All(g => float.IsFinite(g.Exit.X) && float.IsFinite(g.Exit.Y) &&
                float.IsFinite(g.Radius) && g.Radius is > 0 and <= 3));

    public static NetDataWriter Write(RegionEnter value)
    {
        if (!ValidRegion(value)) throw new ArgumentException("Invalid region entry.");
        var writer = CreateWriter(NetworkMessageType.RegionEnter);
        writer.Put(value.Epoch); writer.Put(value.Region);
        writer.Put(value.Exit.X); writer.Put(value.Exit.Y); writer.Put(value.Radius);
        writer.Put(value.GeometryHash);
        writer.Put((byte)(value.AdditionalGates?.Length ?? 0));
        foreach (var gate in value.AdditionalGates ?? [])
        { writer.Put(gate.Exit.X); writer.Put(gate.Exit.Y); writer.Put(gate.Radius); }
        return writer;
    }

    public static bool TryReadRegionEnter(NetDataReader reader, out RegionEnter value)
    {
        value = default;
        if (reader.AvailableBytes > 95 + 12 * (NetworkConstants.MaxRegionGates - 1) || !reader.TryGetULong(out var epoch) ||
            !reader.TryGetString(out var region) || !reader.TryGetFloat(out var x) ||
            !reader.TryGetFloat(out var z) || !reader.TryGetFloat(out var radius) ||
            !reader.TryGetULong(out var geometryHash) || !reader.TryGetByte(out var count) ||
            count >= NetworkConstants.MaxRegionGates || reader.AvailableBytes != count * 12)
            return false;
        var gates = count == 0 ? null : new RegionGate[count];
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryGetFloat(out var gx) || !reader.TryGetFloat(out var gz) || !reader.TryGetFloat(out var gr)) return false;
            gates![i] = new(new(gx, gz), gr);
        }
        var candidate = new RegionEnter(epoch, region, new(x, z), radius, geometryHash, gates);
        if (!ValidRegion(candidate)) return false;
        value = candidate; return true;
    }

    private static bool RegionalMessage(NetworkMessageType type) => Enum.IsDefined(type) &&
        type is not (NetworkMessageType.ClientHello or NetworkMessageType.ServerWelcome or
        NetworkMessageType.ServerReject or NetworkMessageType.RegionEnter or NetworkMessageType.RegionPacket);

    // The caller reuses the envelope for snapshots. LiteNetLib copies data synchronously on Send.
    public static void WrapRegion(NetDataWriter envelope, ulong epoch, NetDataWriter payload)
    {
        if (epoch == 0 || ReferenceEquals(envelope, payload) || payload.Length < 2 ||
            payload.Length > NetworkConstants.MaxGamePacketBytes - RegionEnvelopeBytes ||
            !RegionalMessage((NetworkMessageType)(payload.Data[0] | payload.Data[1] << 8)))
            throw new ArgumentException("Invalid regional packet.");
        envelope.Reset(); envelope.Put((ushort)NetworkMessageType.RegionPacket); envelope.Put(epoch);
        envelope.Put(payload.Data, 0, payload.Length);
    }

    // Called after reading RegionPacket. Leaves the reader at the bounded inner message body.
    public static bool TryReadRegionHeader(NetDataReader reader, out ulong epoch, out NetworkMessageType type)
    {
        epoch = 0; type = default;
        return reader.AvailableBytes <= NetworkConstants.MaxGamePacketBytes - sizeof(ushort) &&
            reader.TryGetULong(out epoch) && epoch > 0 && TryReadMessageType(reader, out type) && RegionalMessage(type);
    }
}
