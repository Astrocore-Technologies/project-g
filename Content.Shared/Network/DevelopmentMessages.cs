using LiteNetLib.Utils;

namespace Content.Shared.Network;

public readonly record struct DevelopmentReviveCommand(uint Sequence);
public readonly record struct DevelopmentTools(bool CanRevive);

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(DevelopmentReviveCommand value)
    {
        if (value.Sequence == 0) throw new ArgumentException("Invalid development command sequence.");
        var writer = CreateWriter(NetworkMessageType.DevelopmentRevive); writer.Put(value.Sequence); return writer;
    }
    public static bool TryReadDevelopmentRevive(NetDataReader reader, out DevelopmentReviveCommand value)
    {
        value = default;
        if (reader.AvailableBytes != 4 || !reader.TryGetUInt(out var sequence) || sequence == 0) return false;
        value = new(sequence); return true;
    }
    public static NetDataWriter Write(DevelopmentTools value)
    {
        var writer = CreateWriter(NetworkMessageType.DevelopmentTools); writer.Put(value.CanRevive); return writer;
    }
    public static bool TryReadDevelopmentTools(NetDataReader reader, out DevelopmentTools value)
    {
        value = default;
        if (reader.AvailableBytes != 1 || !reader.TryGetByte(out var enabled) || enabled > 1) return false;
        value = new(enabled == 1); return true;
    }
}
