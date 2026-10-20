namespace Content.Shared.Network;

public static class NetworkConstants
{
    public const int Port = 9050;
    public const string ConnectionKey = "project-g-dev";
    public const ushort ProtocolVersion = 82; // Camp activities and authored crafting/resource heights.
    public const int CharacterStatCount = 24;
    public const int MaxControlKinds = 14; // Public kinds, not a gameplay cap on control instances.
    public const double ContinuousAttackHeartbeatSeconds = .1;
    public const int MaxRegionGates = 4;
    public const int MaxActiveEchoes = 3;
    public const int EchoRosterPageSize = 8; // Packet budget; ownership has no roster cap.
    public const int MaxInventoryItems = 8;
    public const int ServerTickRate = 20;
    public const int MovementInputRate = 20;
    public const int MovementSnapshotRate = 20;
    public const int MaxHandshakePacketBytes = 256;
    public const int MaxGamePacketBytes = 1200;
    // Packet bound, not an AOI/entity-count limit. One tick can contain multiple packets.
    public const int MaxEntitiesPerSnapshot = 16;
    public const int MaxAbilitySlots = 8;
    public const int MaxAbilityProfiles = 9; // eight bar slots plus the tactical dash
    public const int MaxLearnedSkills = 64; // Maximum progression packet: 1176 bytes, below the 1200-byte budget.
    // Finite flat blockout budget; bit-packed maps fit one regional packet.
    public const int MaxNavigationCells = 6144;
    public const int MaxBuildVersionLength = 64;
    public const int MaxRejectReasonLength = 160;
}
