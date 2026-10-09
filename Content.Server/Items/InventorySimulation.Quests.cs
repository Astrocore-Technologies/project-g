using Content.Server.Persistence;
using Content.Shared.Network;
namespace Content.Server.Items;

public sealed partial class InventorySimulation
{
    internal ushort QuestMaterialCount(NetworkEntityId id, ushort material) => (ushort)((_actors[id].Crafting?.Materials ?? []).FirstOrDefault(m => m.Id == material)?.Quantity ?? 0);
    internal QuestOutcome ChangeQuestMaterial(NetworkEntityId id, ushort material, int delta)
    {
        var actor = _actors[id]; if (actor.TradeSession != 0) return QuestOutcome.Busy;
        var saved = actor.Crafting ?? SavedCrafting.Empty; var old = saved.Materials;
        var index = Array.FindIndex(old, m => m.Id == material); var quantity = (index < 0 ? 0 : old[index].Quantity) + delta;
        if (quantity < 0) return QuestOutcome.MissingMaterials;
        if (quantity > 999 || index < 0 && old.Length >= 16) return QuestOutcome.MaterialFull;
        // Prepare the complete bounded material array before mutation. Quest receipt/XP share its checkpoint.
        var next = old.Where(m => m.Id != material).ToList(); if (quantity > 0) next.Add(new(material, (ushort)quantity));
        next.Sort((a, b) => a.Id.CompareTo(b.Id)); actor.Crafting = saved with { Materials = next.ToArray() }; _dirty.Add(id); return QuestOutcome.Accepted;
    }
}
