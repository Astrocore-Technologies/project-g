namespace Content.Shared.Network;

public static class NetworkConstants
{
    public const int Port = 9050;
    public const string ConnectionKey = "project-g-dev";
    public const ushort ProtocolVersion = 32; // Heights, surface geometry and regional readiness controls.
    public const int MaxRegionGates = 4;
    public const int MaxActiveEchoes = 3;
    public const int MaxInventoryItems = 8;
    public const int ServerTickRate = 20;
    public const int MaxHandshakePacketBytes = 256;
    public const int MaxGamePacketBytes = 1200;
    // Packet bound, not an AOI/entity-count limit. One tick can contain multiple packets.
    public const int MaxEntitiesPerSnapshot = 21;
    public const int MaxAbilitySlots = 8;
    public const int MaxAbilityProfiles = 9; // eight bar slots plus the tactical dash
    public const int MaxLearnedSkills = 16;
    // Finite flat blockout budget; bit-packed maps fit one regional packet.
    public const int MaxNavigationCells = 6144;
    public const int MaxBuildVersionLength = 64;
    public const int MaxRejectReasonLength = 160;
}
