namespace Content.Shared.Network;

public enum NetworkMessageType : ushort
{
    ClientHello = 1,
    ServerWelcome = 2,
    ServerReject = 3
}
