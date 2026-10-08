using System.Security.Cryptography;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Shared.Network;
namespace Content.Server.Items;
public sealed partial class InventorySimulation
{
    internal Func<int> EnhancementRoll {get;set;}=()=>RandomNumberGenerator.GetInt32(100);
    private ItemDefinition EffectiveDefinition(Item i)=>EffectiveDefinition(i.Saved,i.Definition);
    internal ItemDefinition EffectiveDefinition(SavedItem item,ItemDefinition definition)=>item.Enhancement==0?definition:definition with {Modifiers=definition.Modifiers with {MeleeAttack=definition.Modifiers.MeleeAttack+item.Enhancement*catalog.Economy!.AttackPerLevel}};
    internal static string EconomyHash(EconomyCommand c)=>Convert.ToHexString(SHA256.HashData(NetworkProtocol.Write(c).CopyData()));
    public EconomyState EconomyState(NetworkEntityId id,uint tick)
    {var actor=_actors[id];var s=actor.Economy??SavedEconomy.Empty;return new(id,tick,s.LastOperation,s.Coins,s.Effect,actor.Items.Select(i=>new EconomyItem(i.Handle,i.Saved.Condition?.Revision??1,i.Saved.Enhancement)).ToArray());}
    internal CraftOutcome CheckEconomyOperation(NetworkEntityId id,EconomyCommand c,out EconomyEffect effect)
    {
        effect=EconomyEffect.None;var a=_actors[id];var last=a.Economy?.LastOperation??0;
        if(!NetworkProtocol.ValidEconomyCommand(c))return CraftOutcome.InvalidOperation;
        if(c.Operation==last){if(c.QuoteId==0||a.Economy!.PayloadHash!=EconomyHash(c))return CraftOutcome.InvalidOperation;effect=a.Economy.Effect;return CraftOutcome.AlreadyProcessed;}
        if(c.Operation<last)return CraftOutcome.AlreadyProcessed;
        return c.Operation!=last+1?CraftOutcome.InvalidOperation:a.TradeSession!=0?CraftOutcome.Busy:CraftOutcome.Accepted;
    }
    internal SavedItem? EconomyItemSaved(NetworkEntityId id,ulong handle)=>Array.Find(_actors[id].Items,i=>i.Handle==handle)?.Saved;
    internal CraftOutcome PrepareEconomy(NetworkEntityId id,EconomyCommand c,out EconomyQuote quote)
    {
        quote=new(c,0,100,0,"",0,0,15,[]);var balance=catalog.Economy;if(balance is null)return CraftOutcome.Unavailable;
        var actor=_actors[id];var saved=actor.Economy??SavedEconomy.Empty;var item=Array.Find(actor.Items,i=>i.Handle==c.ItemHandle);
        if(c.Action is EconomyAction.List or EconomyAction.Evolve or EconomyAction.Enhance)
        {
            if(item is null||(item.Saved.Condition?.Revision??1)!=c.ItemRevision)return CraftOutcome.Unavailable;
            if(c.Action==EconomyAction.List){if(item.Saved.Bound||item.Definition.Bound)return CraftOutcome.Unavailable;if(item.Saved.EquippedSlot!=EquipmentSlot.None)return CraftOutcome.Busy;quote=quote with {OutputName=item.Definition.Name};}
            else
            {
                if(item.Saved.Condition is not {} condition||condition.Revision==long.MaxValue||item.Definition.Id!=balance.BaseWeapon&&item.Definition.Id!=balance.EvolvedWeapon)return CraftOutcome.Unavailable;
                if(c.Action==EconomyAction.Evolve)
                {if(item.Definition.Id!=balance.BaseWeapon)return CraftOutcome.Unavailable;var evolved=catalog.Items[balance.EvolvedWeapon];quote=quote with {OutputName=evolved.Name,AttackGain=evolved.Modifiers.MeleeAttack-item.Definition.Modifiers.MeleeAttack,Costs=[new(balance.IngotId,(ushort)balance.EvolutionIngots),new(balance.WoodId,(ushort)balance.EvolutionWood)]};}
                else
                {if(item.Saved.Enhancement>=balance.EnhancementChances.Length)return CraftOutcome.Unavailable;quote=quote with {OutputName=item.Definition.Name,Chance=(byte)balance.EnhancementChances[item.Saved.Enhancement],DurabilityLoss=(ushort)balance.FailureDurabilityLoss,AttackGain=balance.AttackPerLevel,Costs=[new(balance.IngotId,(ushort)balance.AttemptIngots)]};}
            }
        }
        else if(c.Action==EconomyAction.SellOre)
        {var gain=checked(c.Quantity*balance.OrePrice);if(saved.Coins>balance.CoinLimit-gain)return CraftOutcome.MaterialFull;quote=quote with {OutputName="Продажа руды",CoinChange=gain,Costs=[new(balance.OreId,c.Quantity)]};}
        foreach(var cost in quote.Costs)if((actor.Crafting??SavedCrafting.Empty).Materials.FirstOrDefault(m=>m.Id==cost.Id)?.Quantity<cost.Quantity || !(actor.Crafting??SavedCrafting.Empty).Materials.Any(m=>m.Id==cost.Id))return CraftOutcome.MissingMaterials;
        return CraftOutcome.Accepted;
    }
    internal CraftOutcome CanReceiveEconomy(NetworkEntityId id,int delta,SavedItem? item)
    {var actor=_actors[id];var coins=(actor.Economy??SavedEconomy.Empty).Coins;if(delta<0&&coins<-(long)delta)return CraftOutcome.MissingMaterials;if(coins+(long)delta>catalog.Economy!.CoinLimit)return CraftOutcome.MaterialFull;if(item is not null){if(!HasRoom(id))return CraftOutcome.InventoryFull;if(actor.Items.Any(i=>i.Saved.InstanceId==item.InstanceId))return CraftOutcome.Unavailable;if(!catalog.Items.ContainsKey(item.DefinitionId))return CraftOutcome.Unavailable;}return CraftOutcome.Accepted;}
    internal EconomyEffect ApplyEconomy(NetworkEntityId id,EconomyCommand c,EconomyQuote quote,SavedItem? received=null)
    {
        var actor=_actors[id];var balance=catalog.Economy!;var wallet=actor.Economy??SavedEconomy.Empty;
        var materials=actor.Crafting??SavedCrafting.Empty;var amounts=materials.Materials.ToDictionary(m=>m.Id,m=>m.Quantity);foreach(var cost in quote.Costs)amounts[cost.Id]-=cost.Quantity;
        var nextMaterials=amounts.Where(m=>m.Value>0).OrderBy(m=>m.Key).Select(m=>new SavedMaterial(m.Key,m.Value)).ToArray();
        var items=actor.Items.ToList();var original=items.Find(i=>i.Handle==c.ItemHandle);var effect=EconomyEffect.None;
        if(c.Action==EconomyAction.List){items.Remove(original!);effect=EconomyEffect.Listed;}
        else if(c.Action is EconomyAction.Evolve or EconomyAction.Enhance)
        {
            var saved=original!.Saved;var condition=saved.Condition!;var definition=original.Definition;
            if(c.Action==EconomyAction.Evolve){definition=catalog.Items[balance.EvolvedWeapon];saved=saved with {DefinitionId=definition.Id,Condition=condition with {Revision=condition.Revision+1}};effect=EconomyEffect.Evolved;}
            else
            {
                var roll=EnhancementRoll();if(roll is <0 or >=100)throw new InvalidOperationException("Invalid server RNG.");
                if(roll<quote.Chance){saved=saved with {Enhancement=(byte)(saved.Enhancement+1),Condition=condition with {Revision=condition.Revision+1}};effect=EconomyEffect.Enhanced;}
                else{var maximum=condition.Maximum-balance.FailureDurabilityLoss;if(maximum<=0){items.Remove(original);effect=EconomyEffect.Destroyed;}else{saved=saved with {Condition=condition with {Maximum=maximum,Current=Math.Min(condition.Current,maximum),Revision=condition.Revision+1}};effect=EconomyEffect.Failed;}}
            }
            if(effect!=EconomyEffect.Destroyed)items[items.IndexOf(original)]=new(original.Handle,saved,definition);
        }
        else if(c.Action==EconomyAction.SellOre)effect=EconomyEffect.SoldOre;
        else if(c.Action is EconomyAction.Buy or EconomyAction.Cancel)
        {if(received is null||_nextHandle==0)throw new InvalidOperationException("Missing escrow item/runtime handle.");items.Add(new(_nextHandle,received,catalog.Items[received.DefinitionId]));effect=c.Action==EconomyAction.Buy?EconomyEffect.Bought:EconomyEffect.Cancelled;}
        else if(c.Action==EconomyAction.Claim)effect=EconomyEffect.Claimed;
        var next=items.ToArray();var equipment=next.Where(i=>i.Saved.EquippedSlot!=EquipmentSlot.None&&i.Saved.Condition?.Current!=0).OrderBy(i=>i.Definition.Slot).Select(EffectiveDefinition).ToArray();
        var profile=combat.PrepareEquipment(primary(id),equipment);var resource=abilities.PrepareEquipment(id,profile.Stats);
        var receipt=new SavedEconomy{LastOperation=c.Operation,Coins=checked(wallet.Coins+quote.CoinChange),PayloadHash=EconomyHash(c),Effect=effect};receipt.Validate();
        // No mutation or RNG outcome is published until the enclosing atomic checkpoint commits.
        actor.Items=next;actor.Crafting=materials with {Materials=nextMaterials};actor.Economy=receipt;
        if(received is not null)_nextHandle++;
        combat.ApplyEquipment(id,profile);abilities.ApplyEquipment(id,resource);_dirty.Add(id);return effect;
    }
}
