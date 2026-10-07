using LiteNetLib.Utils;

namespace Content.Shared.Network;

/// <summary>
/// Defines the exact, bounded binary layout of handshake messages.
/// </summary>
public static class NetworkProtocol
{
    public static NetDataWriter Write(ClientHello message)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            message.BuildVersion.Length,
            NetworkConstants.MaxBuildVersionLength);

        var writer = CreateWriter(NetworkMessageType.ClientHello);
        writer.Put(message.ProtocolVersion);
        writer.Put(message.BuildVersion);
        return writer;
    }

    public static NetDataWriter Write(ServerWelcome message)
    {
        if (!message.PlayerId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(message), "Player ID must be valid.");

        var writer = CreateWriter(NetworkMessageType.ServerWelcome);
        writer.Put(message.PlayerId.Value);
        writer.Put(message.TickRate);
        writer.Put(message.ServerUnixTimeMilliseconds);
        return writer;
    }

    public static NetDataWriter Write(ServerReject message)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            message.Reason.Length,
            NetworkConstants.MaxRejectReasonLength);

        var writer = CreateWriter(NetworkMessageType.ServerReject);
        writer.Put((ushort) message.Code);
        writer.Put(message.Reason);
        return writer;
    }

    public static bool TryReadMessageType(
        NetDataReader reader,
        out NetworkMessageType messageType)
    {
        messageType = default;

        if (!reader.TryGetUShort(out var rawType) ||
            !Enum.IsDefined(typeof(NetworkMessageType), rawType))
        {
            return false;
        }

        messageType = (NetworkMessageType) rawType;
        return true;
    }

    public static bool TryReadClientHello(NetDataReader reader, out ClientHello message)
    {
        message = default;

        if (!reader.TryGetUShort(out var protocolVersion) ||
            !reader.TryGetString(out var buildVersion) ||
            buildVersion is null ||
            buildVersion.Length > NetworkConstants.MaxBuildVersionLength ||
            reader.AvailableBytes != 0)
        {
            return false;
        }

        message = new ClientHello(protocolVersion, buildVersion);
        return true;
    }

    public static bool TryReadServerWelcome(NetDataReader reader, out ServerWelcome message)
    {
        message = default;

        if (!reader.TryGetULong(out var playerId) ||
            !reader.TryGetUShort(out var tickRate) ||
            !reader.TryGetLong(out var serverTime) ||
            playerId == 0 ||
            tickRate == 0 ||
            reader.AvailableBytes != 0)
        {
            return false;
        }

        message = new ServerWelcome(new PlayerId(playerId), tickRate, serverTime);
        return true;
    }

    public static bool TryReadServerReject(NetDataReader reader, out ServerReject message)
    {
        message = default;

        if (!reader.TryGetUShort(out var rawCode) ||
            !Enum.IsDefined(typeof(HandshakeRejectCode), rawCode) ||
            !reader.TryGetString(out var reason) ||
            reason is null ||
            reason.Length > NetworkConstants.MaxRejectReasonLength ||
            reader.AvailableBytes != 0)
        {
            return false;
        }

        message = new ServerReject((HandshakeRejectCode) rawCode, reason);
        return true;
    }

    private static NetDataWriter CreateWriter(NetworkMessageType messageType)
    {
        var writer = new NetDataWriter();
        writer.Put((ushort) messageType);
        return writer;
    }
}
