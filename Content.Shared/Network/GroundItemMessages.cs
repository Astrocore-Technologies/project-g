using System.Numerics;

namespace Content.Shared.Network;

public enum PickupOutcome : byte { Accepted, Missing, OutOfRange, Blocked, InvalidState, InventoryFull, RateLimited, Channeling, Interrupted }
public readonly record struct PickupCommand(uint Sequence, ulong Handle);
public readonly record struct PickupResult(uint Sequence, uint ServerTick, PickupOutcome Outcome);
public readonly record struct GroundItemSpawn(ulong Handle, uint ServerTick, Vector2 Position, string Name, EquipmentSlot Slot);
public readonly record struct GroundItemDespawn(ulong Handle, uint ServerTick);
