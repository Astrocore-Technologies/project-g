namespace Content.Shared.Network;

public readonly record struct ClientHello(ushort ProtocolVersion, string BuildVersion, string DevelopmentToken = "");

public readonly record struct ServerWelcome(
    PlayerId PlayerId,
    ushort TickRate,
    long ServerUnixTimeMilliseconds,
    string DevelopmentToken = "");

public readonly record struct ServerReject(HandshakeRejectCode Code, string Reason);

public enum HandshakeRejectCode : ushort
{
    MalformedPacket = 1,
    UnsupportedProtocol = 2,
    UnexpectedMessage = 3,
    HandshakeAlreadyCompleted = 4,
    InvalidIdentity = 5,
    CharacterInUse = 6,
    PersistenceUnavailable = 7,
    DevelopmentOnly = 8,
    ServerBusy = 9
}
