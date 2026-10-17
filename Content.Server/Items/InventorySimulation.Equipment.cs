using Content.Shared.Network;

namespace Content.Server.Items;

public sealed partial class InventorySimulation
{
    private bool CompatibleEquipment(Actor actor,Item? changed,EquipmentSlot desired)
    {
        var shield=false;var twoHanded=false;
        foreach(var item in actor.Items)
        {
            var slot=item==changed ? desired : item.Saved.EquippedSlot;
            if(changed is not null && item!=changed && desired!=EquipmentSlot.None && slot==desired)continue;
            shield|=slot==EquipmentSlot.Offhand;
            if(slot==EquipmentSlot.Weapon)twoHanded=catalog.Weapons[item.Definition.WeaponId!].TwoHanded;
        }
        // Broken equipment retains its occupied slot, while functional modifiers are filtered separately.
        return !shield || !twoHanded;
    }
}
