namespace Content.Shared.Network;

public enum NetworkMessageType : ushort
{
    ClientHello = 1,
    ServerWelcome = 2,
    ServerReject = 3,
    PlayerSpawn = 10,
    PlayerDespawn = 11,
    MoveCommand = 12,
    WorldSnapshot = 13
}
