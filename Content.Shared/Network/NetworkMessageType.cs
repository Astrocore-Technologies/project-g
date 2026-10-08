namespace Content.Shared.Network;

public enum NetworkMessageType : ushort
{
    ClientHello = 1,
    ServerWelcome = 2,
    ServerReject = 3,
    PlayerSpawn = 10,
    PlayerDespawn = 11,
    MoveCommand = 12,
    WorldSnapshot = 13,
    RegionNavigation = 14,
    AttackCommand = 15,
    AttackResult = 16,
    AttackEvent = 17,
    CombatState = 18,
    AbilityCommand = 19,
    AbilityResult = 20,
    AbilityLoadout = 21,
    AbilityEffectState = 22,
    AbilityHit = 23,
    NpcWindup = 24,
    NpcArea = 25,
    InventoryCommand = 26,
    InventoryResult = 27,
    InventoryState = 28,
    PickupCommand = 29,
    PickupResult = 30,
    GroundItemSpawn = 31,
    GroundItemDespawn = 32,
    DevelopmentRevive = 33,
    DevelopmentTools = 34
}
