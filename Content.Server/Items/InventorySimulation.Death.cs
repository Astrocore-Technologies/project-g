using Content.Server.Persistence;
using Content.Shared.Network;
namespace Content.Server.Items;
public sealed partial class InventorySimulation
{
    internal SavedItem[] DeathCandidates(NetworkEntityId id)=>_actors[id].Items.Where(i=>i.Saved.EquippedSlot!=EquipmentSlot.None&&!i.Saved.Bound&&!i.Definition.Bound).Select(i=>i.Saved).OrderBy(i=>i.InstanceId).ToArray();
    internal SavedItem RemoveDeathItem(NetworkEntityId id,Guid uuid)
    {
        var actor=_actors[id];var item=actor.Items.Single(i=>i.Saved.InstanceId==uuid);var result=item.Saved with {EquippedSlot=EquipmentSlot.None};
        actor.Items=actor.Items.Where(i=>i!=item).ToArray();Apply(id,actor,null,EquipmentSlot.None);_dirty.Add(id);return result;
    }
}
