namespace Content.Shared.Network;

public static class NetworkConstants
{
    public const int Port = 9050;
    public const string ConnectionKey = "project-g-dev";
    public const ushort ProtocolVersion = 1;
    public const int ServerTickRate = 20;
    public const int MaxHandshakePacketBytes = 256;
    public const int MaxBuildVersionLength = 64;
    public const int MaxRejectReasonLength = 160;
}
