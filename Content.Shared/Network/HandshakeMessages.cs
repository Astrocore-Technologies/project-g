namespace Content.Shared.Network;

public readonly record struct ClientHello(ushort ProtocolVersion, string BuildVersion);

public readonly record struct ServerWelcome(
    PlayerId PlayerId,
    ushort TickRate,
    long ServerUnixTimeMilliseconds);

public readonly record struct ServerReject(HandshakeRejectCode Code, string Reason);

public enum HandshakeRejectCode : ushort
{
    MalformedPacket = 1,
    UnsupportedProtocol = 2,
    UnexpectedMessage = 3,
    HandshakeAlreadyCompleted = 4
}
