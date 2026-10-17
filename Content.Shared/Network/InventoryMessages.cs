namespace Content.Shared.Network;

public enum EquipmentSlot : byte { None, Weapon, Armor, Offhand }
public enum InventoryAction : byte { Equip = 1, Unequip = 2 }
public enum InventoryOutcome : byte { Accepted, NotOwned, InvalidState, Busy, RateLimited, IncompatibleEquipment }
public readonly record struct InventoryCommand(uint Sequence, InventoryAction Action, ulong ItemHandle);
public readonly record struct InventoryResult(uint Sequence, uint ServerTick, InventoryOutcome Outcome);
public readonly record struct InventoryEntry(ulong Handle, string Name, EquipmentSlot Slot, bool Equipped,
    double AttackBonus, double DefenseBonus, double HealthBonus, double BlockAbsorptionBonus=0);
public readonly record struct InventoryState(NetworkEntityId EntityId, uint ServerTick, IReadOnlyList<InventoryEntry> Items);
