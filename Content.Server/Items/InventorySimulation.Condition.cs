using System.Security.Cryptography;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Shared.Network;
namespace Content.Server.Items;
public sealed partial class InventorySimulation
{
    private static SavedItem ChangeSlot(SavedItem item,EquipmentSlot slot)
    { if(item.EquippedSlot==slot) return item; return item with { EquippedSlot=slot,Condition=item.Condition is { } state ? state with { Revision=checked(state.Revision+1) } : null }; }
    private static SavedItem NormalizeCondition(SavedItem item,ItemDefinition definition)
    {
        if(definition.Condition is not { } balance) { if(item.Condition is not null) throw new InvalidDataException("Condition needs content migration."); return item; }
        if(item.Condition is { } saved) { saved.Validate(); if(saved.Maximum>balance.Maximum) throw new InvalidDataException("Maximum durability needs migration."); return item; }
        return item with { Condition=new() { Current=balance.Maximum,Maximum=balance.Maximum,Revision=1 } };
    }
    public bool WeaponUsable(NetworkEntityId id)
    { if(!_actors.TryGetValue(id,out var actor)) return true; foreach(var item in actor.Items) if(item.Saved.EquippedSlot==EquipmentSlot.Weapon && item.Saved.Condition?.Current==0) return false; return true; }
    public void Wear(AttackEvent action)
    {
        if(action.Damage<=0 || !_actors.TryGetValue(action.AttackerId,out var actor) || actor.LastWearSequence==action.Sequence) return;
        actor.LastWearSequence=action.Sequence;
        foreach(var item in actor.Items) if(item.Saved.EquippedSlot==EquipmentSlot.Weapon && item.Saved.Condition is { Current:>0 } state)
        {
            if(state.Revision==long.MaxValue) throw new InvalidOperationException("Item revision exhausted.");
            item.Saved=item.Saved with { Condition=state with { Current=Math.Max(0,state.Current-item.Definition.Condition!.WearPerHit),Revision=state.Revision+1 } };
            if(item.Saved.Condition.Current==0) RefreshStats(action.AttackerId);
            _dirty.Add(action.AttackerId); break;
        }
    }
    public ItemConditionState ConditionState(NetworkEntityId id,uint tick) => new(id,tick,_actors[id].Maintenance?.LastOperation ?? 0,
        _actors[id].Items.Where(i=>i.Saved.Condition is not null).Select(i=>new ItemConditionEntry(i.Handle,(ushort)i.Saved.Condition!.Current,(ushort)i.Saved.Condition.Maximum,i.Saved.Condition.Revision)).ToArray());
    internal bool TryRepairInfo(NetworkEntityId id,ulong handle,out ulong revision,out ushort material,out ushort quantity)
    {
        revision=0; material=quantity=0; var item=Array.Find(_actors[id].Items,i=>i.Handle==handle);
        if(item?.Saved.Condition is not { } state || state.Current==state.Maximum) return false;
        revision=state.Revision; var balance=item.Definition.Condition!; material=balance.RepairMaterialId; quantity=(ushort)((state.Maximum-state.Current+balance.DurabilityPerMaterial-1)/balance.DurabilityPerMaterial); return true;
    }
    internal static string RepairHash(RepairCommand command) => Convert.ToHexString(SHA256.HashData(NetworkProtocol.Write(command).CopyData()));
    internal CraftOutcome CheckRepairOperation(NetworkEntityId id,RepairCommand command)
    {
        var receipt=_actors[id].Maintenance; var last=receipt?.LastOperation ?? 0;
        if(command.Operation==0 || command.Operation>long.MaxValue) return CraftOutcome.InvalidOperation;
        if(command.Operation==last) return command.QuoteId!=0 && receipt!.PayloadHash==RepairHash(command) ? CraftOutcome.AlreadyProcessed : CraftOutcome.InvalidOperation;
        if(command.Operation<last) return CraftOutcome.AlreadyProcessed;
        return command.Operation!=last+1 ? CraftOutcome.InvalidOperation : _actors[id].TradeSession!=0 ? CraftOutcome.Busy : CraftOutcome.Accepted;
    }
    internal CraftOutcome Repair(NetworkEntityId id,RepairCommand command,ushort material,ushort quantity)
    {
        var actor=_actors[id]; var item=Array.Find(actor.Items,i=>i.Handle==command.ItemHandle);
        if(item?.Saved.Condition is not { } state || state.Current==state.Maximum || state.Revision!=command.ItemRevision) return CraftOutcome.Unavailable;
        if(state.Revision==long.MaxValue) return CraftOutcome.Unavailable;
        var saved=actor.Crafting ?? SavedCrafting.Empty; var index=Array.FindIndex(saved.Materials,m=>m.Id==material);
        if(index<0 || saved.Materials[index].Quantity<quantity) return CraftOutcome.MissingMaterials;
        var next=saved.Materials.Select(m=>m.Id==material ? m with { Quantity=m.Quantity-quantity } : m).Where(m=>m.Quantity>0).ToArray();
        var receipt=new SavedMaintenance { LastOperation=command.Operation,PayloadHash=RepairHash(command),ItemId=item.Saved.InstanceId,ItemRevision=state.Revision+1,MaterialId=material,Quantity=quantity }; receipt.Validate();
        item.Saved=item.Saved with { Condition=state with { Current=state.Maximum,Revision=state.Revision+1 } }; actor.Crafting=saved with { Materials=next }; actor.Maintenance=receipt;
        RefreshStats(id); _dirty.Add(id); return CraftOutcome.Accepted;
    }
}
