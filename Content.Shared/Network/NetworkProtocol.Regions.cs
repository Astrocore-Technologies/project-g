using System.Numerics;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

// Public boundary geometry only; no destination content or unexplored map is sent.
public readonly record struct RegionEnter(ulong Epoch, string Region, Vector2 Exit, float Radius);

public static partial class NetworkProtocol
{
    public const int RegionEnvelopeBytes = sizeof(ushort) + sizeof(ulong);

    private static bool ValidRegion(RegionEnter value) => value.Epoch > 0 &&
        value.Region is { Length: > 0 and <= 64 } &&
        value.Region.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') &&
        float.IsFinite(value.Exit.X) && float.IsFinite(value.Exit.Y) &&
        float.IsFinite(value.Radius) && value.Radius is > 0 and <= 3;

    public static NetDataWriter Write(RegionEnter value)
    {
        if (!ValidRegion(value)) throw new ArgumentException("Invalid region entry.");
        var writer = CreateWriter(NetworkMessageType.RegionEnter);
        writer.Put(value.Epoch); writer.Put(value.Region);
        writer.Put(value.Exit.X); writer.Put(value.Exit.Y); writer.Put(value.Radius);
        return writer;
    }

    public static bool TryReadRegionEnter(NetDataReader reader, out RegionEnter value)
    {
        value = default;
        if (reader.AvailableBytes > 86 || !reader.TryGetULong(out var epoch) ||
            !reader.TryGetString(out var region) || !reader.TryGetFloat(out var x) ||
            !reader.TryGetFloat(out var z) || !reader.TryGetFloat(out var radius) || reader.AvailableBytes != 0)
            return false;
        var candidate = new RegionEnter(epoch, region, new(x, z), radius);
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
