namespace Content.Shared.Network;

public static class NetworkConstants
{
    public const int Port = 9050;
    public const string ConnectionKey = "project-g-dev";
    public const ushort ProtocolVersion = 3;
    public const int ServerTickRate = 20;
    public const int MaxHandshakePacketBytes = 256;
    public const int MaxGamePacketBytes = 1200;
    // Packet bound, not an AOI/entity-count limit. One tick can contain multiple packets.
    public const int MaxEntitiesPerSnapshot = 32;
    public const int MaxBuildVersionLength = 64;
    public const int MaxRejectReasonLength = 160;
}
