using Content.Server.Persistence;
using Content.Shared.Network;

namespace Content.Server.Items;

public sealed partial class InventorySimulation
{
    internal bool HasFunctionalWeapon(NetworkEntityId id)
    {
        if(!_actors.TryGetValue(id,out var actor))return true; // Creature-owned profiles have no item inventory.
        foreach(var item in actor.Items)
            if(item.Saved.EquippedSlot==EquipmentSlot.Weapon && item.Saved.Condition?.Current!=0)return true;
        return false;
    }

    internal bool HasItem(NetworkEntityId id, string definitionId) => _actors.TryGetValue(id, out var a) &&
        a.Items.Any(i => i.Definition.Id == definitionId);

    internal bool HasEquippedItem(NetworkEntityId id, string definitionId) => _actors.TryGetValue(id, out var a) &&
        a.Items.Any(i => i.Definition.Id == definitionId && i.Saved.EquippedSlot == EquipmentSlot.Weapon && i.Saved.Condition?.Current != 0);

    internal bool HasEquippedSword(NetworkEntityId id) => _actors.TryGetValue(id, out var a) &&
        a.Items.Any(i => i.Saved.EquippedSlot == EquipmentSlot.Weapon && i.Saved.Condition?.Current != 0 &&
            i.Definition.WeaponId is { } weapon && catalog.Weapons[weapon].IsSword);

    // Bound and non-sellable; requesting a replacement never duplicates an owned copy.
    internal bool GrantTrainingItem(NetworkEntityId id, string definitionId) => HasItem(id, definitionId) ||
        AddPickedItem(id, new SavedItem(Guid.NewGuid(), definitionId, EquipmentSlot.None) { Bound = true });
}
