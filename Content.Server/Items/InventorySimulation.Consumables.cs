using Content.Shared.Network;

namespace Content.Server.Items;

public sealed partial class InventorySimulation
{
    internal bool CanConsume(NetworkEntityId owner,string definitionId,int quantity)
    {
        if(quantity<=0 || !_actors.TryGetValue(owner,out var actor) || actor.TradeSession!=0)return false;
        var found=0;
        foreach(var item in actor.Items)
            if(item.Definition.Id==definitionId && item.Saved.EquippedSlot==EquipmentSlot.None && ++found==quantity)return true;
        return false;
    }

    // Preparation/validation precede this mutation. A single fenced character save also writes the recovered Echo.
    internal bool TryConsume(NetworkEntityId owner,string definitionId,int quantity)
    {
        if(!CanConsume(owner,definitionId,quantity))return false;
        var actor=_actors[owner];var remaining=new Item[actor.Items.Length-quantity];var index=0;
        foreach(var item in actor.Items)
        {
            if(quantity>0 && item.Definition.Id==definitionId && item.Saved.EquippedSlot==EquipmentSlot.None){quantity--;continue;}
            remaining[index++]=item;
        }
        actor.Items=remaining;IndexItems(owner,actor);_dirty.Add(owner);return true;
    }
}
