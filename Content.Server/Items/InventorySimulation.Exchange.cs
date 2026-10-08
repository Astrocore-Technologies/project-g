using Content.Server.Persistence;
using Content.Shared.Network;
namespace Content.Server.Items;
public sealed record ExchangeOffer(IReadOnlyList<ulong> Items,IReadOnlyList<MaterialAmount> Materials)
{ public static ExchangeOffer Empty => new([],[]); }
public sealed partial class InventorySimulation
{
    internal CraftOutcome ValidateOffer(NetworkEntityId id,ExchangeOffer offer)
    {
        if(!_actors.TryGetValue(id,out var actor) || offer.Items.Count>8 || offer.Materials.Count>16 || offer.Items.Any(h=>h==0) || offer.Items.Distinct().Count()!=offer.Items.Count || offer.Materials.Any(m=>m.Id==0 || m.Quantity is 0 or >999) || offer.Materials.Select(m=>m.Id).Distinct().Count()!=offer.Materials.Count) return CraftOutcome.InvalidOperation;
        foreach(var handle in offer.Items) if(!actor.Items.Any(i=>i.Handle==handle && i.Saved.EquippedSlot==EquipmentSlot.None)) return CraftOutcome.Unavailable;
        var materials=actor.Crafting?.Materials ?? [];
        foreach(var amount in offer.Materials) if(!materials.Any(m=>m.Id==amount.Id && m.Quantity>=amount.Quantity)) return CraftOutcome.MissingMaterials;
        return CraftOutcome.Accepted;
    }
    internal bool HoldExchange(NetworkEntityId id,ulong session)
    { var actor=_actors[id]; if(session==0 || actor.TradeSession!=0 && actor.TradeSession!=session) return false; actor.TradeSession=session; return true; }
    internal void ReleaseExchange(NetworkEntityId id,ulong session) { if(_actors.TryGetValue(id,out var actor) && actor.TradeSession==session) actor.TradeSession=0; }
    internal InventoryEntry[] OfferPreview(NetworkEntityId id,ExchangeOffer offer) => State(id,0).Items.Where(i=>offer.Items.Contains(i.Handle)).ToArray();
    private static SavedMaterial[]? ExchangeMaterials(Actor actor,ExchangeOffer sent,ExchangeOffer received)
    {
        var amounts=(actor.Crafting?.Materials ?? []).ToDictionary(m=>m.Id,m=>m.Quantity);
        foreach(var m in sent.Materials) amounts[m.Id]-=m.Quantity;
        foreach(var m in received.Materials) { var next=amounts.GetValueOrDefault(m.Id)+m.Quantity; if(next>999) return null; amounts[m.Id]=next; }
        var values=amounts.Where(m=>m.Value>0).OrderBy(m=>m.Key).Select(m=>new SavedMaterial(m.Key,m.Value)).ToArray(); return values.Length<=16 ? values : null;
    }
    internal CraftOutcome Exchange(NetworkEntityId a,NetworkEntityId b,ulong session,ExchangeOffer first,ExchangeOffer second)
    {
        if(a==b || !_actors.TryGetValue(a,out var left) || !_actors.TryGetValue(b,out var right) || left.TradeSession!=session || right.TradeSession!=session) return CraftOutcome.InvalidOperation;
        var result=ValidateOffer(a,first); if(result!=CraftOutcome.Accepted) return result; result=ValidateOffer(b,second); if(result!=CraftOutcome.Accepted) return result;
        if(left.Items.Length-first.Items.Count+second.Items.Count>8 || right.Items.Length-second.Items.Count+first.Items.Count>8) return CraftOutcome.InventoryFull;
        var lm=ExchangeMaterials(left,first,second); var rm=ExchangeMaterials(right,second,first); if(lm is null || rm is null) return CraftOutcome.MaterialFull;
        var li=left.Items.Where(i=>!first.Items.Contains(i.Handle)).ToList(); var ri=right.Items.Where(i=>!second.Items.Contains(i.Handle)).ToList();
        var transfers=first.Items.Count+second.Items.Count; if(_nextHandle==0 || _nextHandle>ulong.MaxValue-(ulong)transfers) throw new InvalidOperationException("Item handles exhausted.");
        foreach(var item in right.Items) if(second.Items.Contains(item.Handle)) li.Add(new(_nextHandle++,item.Saved,item.Definition));
        foreach(var item in left.Items) if(first.Items.Contains(item.Handle)) ri.Add(new(_nextHandle++,item.Saved,item.Definition));
        // All validation/preparation succeeds before either owner is mutated. Database fences both owners in one batch.
        left.Items=li.ToArray(); right.Items=ri.ToArray(); left.Crafting=(left.Crafting ?? SavedCrafting.Empty) with { Materials=lm }; right.Crafting=(right.Crafting ?? SavedCrafting.Empty) with { Materials=rm }; left.TradeSession=right.TradeSession=0;
        _dirty.Add(a); _dirty.Add(b); return CraftOutcome.Accepted;
    }
}
