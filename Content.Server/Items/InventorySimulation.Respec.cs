using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Items;

public sealed partial class InventorySimulation
{
    internal readonly record struct PreparedStatReset(CombatEquipmentProfile Combat,AbilityActor Abilities,SavedEconomy Wallet);
    internal PreparedStatReset PrepareStatReset(NetworkEntityId id,BaseStats stats,int price)
    {
        var actor=_actors[id];var wallet=actor.Economy??SavedEconomy.Empty;
        if(price<0||wallet.Coins<price||actor.TradeSession!=0)throw new ArgumentException("Reset cannot debit this wallet.");
        var equipment=actor.Items.Where(i=>i.Saved.EquippedSlot!=EquipmentSlot.None&&i.Saved.Condition?.Current!=0)
            .OrderBy(i=>i.Definition.Slot).Select(EffectiveDefinition).ToArray();
        var profile=combat.PrepareEquipment(stats,equipment);
        var resources=abilities.PrepareEquipment(id,combat.WithEffects(id,profile.Stats,profile.Weapon));
        var next=wallet with {Coins=wallet.Coins-price};next.Validate();return new(profile,resources,next);
    }
    internal void CommitStatReset(NetworkEntityId id,PreparedStatReset prepared)
    {
        _actors[id].Economy=prepared.Wallet;
        combat.ApplyEquipment(id,prepared.Combat);abilities.ApplyEquipment(id,prepared.Abilities);_dirty.Add(id);
    }
}
