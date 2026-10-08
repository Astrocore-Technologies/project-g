namespace Content.Shared.Network;

public static class NetworkConstants
{
    public const int Port = 9050;
    public const string ConnectionKey = "project-g-dev";
    public const ushort ProtocolVersion = 22;
    public const int MaxActiveEchoes = 3;
    public const int MaxInventoryItems = 8;
    public const int ServerTickRate = 20;
    public const int MaxHandshakePacketBytes = 256;
    public const int MaxGamePacketBytes = 1200;
    // Packet bound, not an AOI/entity-count limit. One tick can contain multiple packets.
    public const int MaxEntitiesPerSnapshot = 24;
    public const int MaxAbilitySlots = 8;
    public const int MaxAbilityProfiles = 9; // eight bar slots plus the tactical dash
    public const int MaxLearnedSkills = 16;
    public const int MaxNavigationCells = 1024;
    public const int MaxBuildVersionLength = 64;
    public const int MaxRejectReasonLength = 160;
}
