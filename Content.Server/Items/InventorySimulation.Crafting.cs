using Content.Server.Crafting;
using Content.Server.Persistence;
using Content.Shared.Network;
namespace Content.Server.Items;
public sealed partial class InventorySimulation
{
    private void ValidateCrafting(SavedCrafting? saved)
    {
        if(saved is null) return; saved.Validate();
        if(catalog.Crafting is not { } definition || saved.Materials.Any(m=>!definition.Materials.Any(d=>d.Id==m.Id))) throw new InvalidDataException("Saved materials require content migration.");
    }
    private SavedCrafting? CaptureCrafting(NetworkEntityId id)
    { var actor=_actors[id]; return actor.Crafting is { } saved ? saved with { CooldownSeconds=Math.Max(0,actor.CraftReadyAt-combat.Time) } : null; }
    public SavedCrafting CraftingState(NetworkEntityId id) => CaptureCrafting(id) ?? SavedCrafting.Empty;
    private CraftOutcome CheckOperation(Actor actor,ulong operation)
    {
        var last=actor.Crafting?.LastOperation ?? 0;
        return operation==0 || operation>long.MaxValue ? CraftOutcome.InvalidOperation
            : operation<=last ? CraftOutcome.AlreadyProcessed : operation!=last+1 ? CraftOutcome.InvalidOperation
            : actor.TradeSession!=0 ? CraftOutcome.Busy : combat.Time<actor.CraftReadyAt ? CraftOutcome.Cooldown : CraftOutcome.Accepted;
    }
    public CraftOutcome CheckCraftOperation(NetworkEntityId id,ulong operation) => CheckOperation(_actors[id],operation);
    public CraftOutcome GatherMaterial(NetworkEntityId id,ulong operation,ushort material,float cooldown)
    {
        var actor=_actors[id]; var outcome=CheckOperation(actor,operation); if(outcome!=CraftOutcome.Accepted) return outcome;
        if(catalog.Crafting is not { } definition || !definition.Materials.Any(m=>m.Id==material)) return CraftOutcome.Unavailable;
        var saved=actor.Crafting ?? SavedCrafting.Empty; var old=saved.Materials; var index=Array.FindIndex(old,m=>m.Id==material);
        if(index>=0 && old[index].Quantity==999 || index<0 && old.Length==16) return CraftOutcome.MaterialFull;
        var next=index<0 ? old.Append(new SavedMaterial(material,1)).OrderBy(m=>m.Id).ToArray() : (SavedMaterial[])old.Clone();
        if(index>=0) next[index]=old[index] with { Quantity=old[index].Quantity+1 };
        actor.Crafting=saved with { LastOperation=operation,Materials=next,CooldownSeconds=0 }; actor.CraftReadyAt=combat.Time+cooldown; _dirty.Add(id); return CraftOutcome.Accepted;
    }
    public CraftOutcome MakeRecipe(NetworkEntityId id,ulong operation,RecipeDefinition recipe,float cooldown)
    {
        var actor=_actors[id]; var outcome=CheckOperation(actor,operation); if(outcome!=CraftOutcome.Accepted) return outcome;
        var saved=actor.Crafting ?? SavedCrafting.Empty;
        var amounts=saved.Materials.ToDictionary(m=>m.Id,m=>m.Quantity);
        foreach(var cost in recipe.Costs) if(amounts.GetValueOrDefault(cost.Id)<cost.Quantity) return CraftOutcome.MissingMaterials;
        if(recipe.OutputItemId is not null && actor.Items.Length>=NetworkConstants.MaxInventoryItems) return CraftOutcome.InventoryFull;
        foreach(var cost in recipe.Costs) amounts[cost.Id]-=cost.Quantity;
        if(recipe.OutputItemId is null)
        { var quantity=amounts.GetValueOrDefault(recipe.OutputMaterialId)+recipe.OutputQuantity; if(quantity>999) return CraftOutcome.MaterialFull; amounts[recipe.OutputMaterialId]=quantity; }
        var next=amounts.Where(m=>m.Value>0).OrderBy(m=>m.Key).Select(m=>new SavedMaterial(m.Key,m.Value)).ToArray(); if(next.Length>16) return CraftOutcome.MaterialFull;
        Item[]? items=null;
        if(recipe.OutputItemId is { } output)
        {
            if(!catalog.Items.TryGetValue(output,out var definition)) return CraftOutcome.Unavailable;
            if(_nextHandle==0) throw new InvalidOperationException("Runtime item handles exhausted.");
            items=new Item[actor.Items.Length+1]; Array.Copy(actor.Items,items,actor.Items.Length);
            items[^1]=new(_nextHandle,NormalizeCondition(new SavedItem(Guid.NewGuid(),output,EquipmentSlot.None),definition),definition);
        }
        // Everything is prepared before one owner-bound mutation; the checkpoint persists it with XP/world stock.
        if(items is not null) { _nextHandle++; actor.Items=items; }
        actor.Crafting=saved with { LastOperation=operation,Materials=next,CooldownSeconds=0 }; actor.CraftReadyAt=combat.Time+cooldown; _dirty.Add(id); return CraftOutcome.Accepted;
    }
}
