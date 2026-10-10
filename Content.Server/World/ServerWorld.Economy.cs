using System.Numerics;
using Content.Server.Persistence;
using Content.Shared.Network;
namespace Content.Server.World;
public sealed partial class ServerWorld
{
    private readonly Dictionary<int,EconomyCommand> _economyPending=new();
    private readonly Dictionary<int,uint> _economyRequestTicks=new();
    private readonly Dictionary<int,(EconomyQuote Quote,double Expires)> _economyQuotes=new();
    private readonly Dictionary<NetworkEntityId,EconomyQuote> _economyQuoteResults=new();
    private readonly Dictionary<NetworkEntityId,EconomyResult> _economyResults=new();
    public IReadOnlyDictionary<NetworkEntityId,EconomyQuote> EconomyQuotes=>_economyQuoteResults;
    public IReadOnlyDictionary<NetworkEntityId,EconomyResult> EconomyResults=>_economyResults;
    public bool HasEconomy=>HasCrafting&&_progressionCatalog?.Economy is not null;
    public bool MarketDirty {get;private set;}
    internal Func<long> ResourceClock {get;set;}=()=>DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private Guid EconomyOwner(int connection)=>Guid.TryParseExact(_persistentActors.GetValueOrDefault(connection),"N",out var id)?id:Guid.Empty;
    public bool TryQueueEconomy(int connection,EconomyCommand command)
    {
        if(!HasEconomy || !_playersByConnection.TryGetValue(connection,out var player)||!NetworkProtocol.ValidEconomyCommand(command))return false;
        if(_economyRequestTicks.GetValueOrDefault(connection,uint.MaxValue)==Tick){_economyPending.Remove(connection);_economyResults[player.EntityId]=new(command.Operation,Tick+1,CraftOutcome.RateLimited,EconomyEffect.None);return false;}
        GroundItems?.CancelChannel(player.EntityId,Tick); _economyRequestTicks[connection]=Tick;_economyPending[connection]=command;return true;
    }
    private void ValidateMarketContent()
    {
        if(_nodeState.Market is not {} market)return;
        if(!HasEconomy)throw new InvalidDataException("Market requires economy content.");
        var b=_progressionCatalog!.Economy!;
        if(market.Listings.Length>b.ListingLimit || market.Listings.GroupBy(l=>l.Seller).Any(g=>g.Count()>b.ListingsPerSeller) || market.Credits.Any(c=>c.Coins>b.CoinLimit))throw new InvalidDataException("Market balance migration required.");
        foreach(var l in market.Listings)
        {
            if(l.Price>b.CoinLimit || !_progressionCatalog.Items.TryGetValue(l.Item.DefinitionId,out var definition) || l.Item.Condition is {} condition && (definition.Condition is null || condition.Maximum>definition.Condition.Maximum) || l.Item.Enhancement>0 && (l.Item.DefinitionId!=b.BaseWeapon&&l.Item.DefinitionId!=b.EvolvedWeapon || l.Item.Enhancement>b.EnhancementChances.Length))throw new InvalidDataException("Escrow item migration required.");
            if(definition.Condition is not null && l.Item.Condition is null)throw new InvalidDataException("Escrow condition migration required.");
        }
    }
    public bool CanViewMarket(int connection)=>HasEconomy && _playersByConnection.TryGetValue(connection,out var p) && Vector3.DistanceSquared(p.Foot, GroundFoot(_crafting!.StationPosition))<=_crafting.InteractionRange*_crafting.InteractionRange && Navigation.ClearAttack(p.Foot, GroundFoot(_crafting.StationPosition));
    public MarketState MarketState(int connection,bool includeListings=true)
    {
        var id=_playersByConnection[connection].EntityId;var owner=EconomyOwner(connection);var market=_nodeState.Market??SavedMarket.Empty;
        return new(id,Tick,market.Credits.FirstOrDefault(c=>c.Seller==owner)?.Coins??0,(includeListings?market.Listings:[]).Select(l=>
        {var d=Inventory!.EffectiveDefinition(l.Item,_progressionCatalog!.Items[l.Item.DefinitionId]);var s=l.Item.Condition;return new MarketListing(l.Id,l.Price,l.Seller==owner,new(l.Id,d.Name+(l.Item.Enhancement>0?" +"+l.Item.Enhancement:""),d.Slot,d.Modifiers.MeleeAttack,d.Modifiers.PhysicalDefense,d.Modifiers.MaxHealth,(ushort)(s?.Current??0),(ushort)(s?.Maximum??0)));}).ToArray());
    }
    private CraftOutcome PrepareMarket(int connection,EconomyCommand command,ref EconomyQuote quote,out SavedListing? listing,out int credit)
    {
        listing=null;credit=0;var balance=_progressionCatalog!.Economy!;var market=_nodeState.Market??SavedMarket.Empty;var owner=EconomyOwner(connection);var id=_playersByConnection[connection].EntityId;
        if(command.Action is not (EconomyAction.List or EconomyAction.Buy or EconomyAction.Cancel or EconomyAction.Claim))return CraftOutcome.Accepted;
        if(owner==Guid.Empty)return CraftOutcome.Unavailable;
        if(command.Action==EconomyAction.List)
        {if(command.Price>balance.CoinLimit)return CraftOutcome.InvalidOperation;if(market.NextListing==long.MaxValue || market.Listings.Length>=balance.ListingLimit || market.Listings.Count(l=>l.Seller==owner)>=balance.ListingsPerSeller)return CraftOutcome.InventoryFull;return CraftOutcome.Accepted;}
        if(command.Action==EconomyAction.Claim)
        {credit=market.Credits.FirstOrDefault(c=>c.Seller==owner)?.Coins??0;if(credit==0)return CraftOutcome.Unavailable;quote=quote with {OutputName="Получить выручку",CoinChange=credit};return Inventory!.CanReceiveEconomy(id,credit,null);}
        listing=Array.Find(market.Listings,l=>l.Id==command.ListingId);if(listing is null)return CraftOutcome.Unavailable;
        if(command.Action==EconomyAction.Buy)
        {
            if(listing.Seller==owner)return CraftOutcome.InvalidOperation;
            var seller=listing.Seller;var existing=market.Credits.FirstOrDefault(c=>c.Seller==seller)?.Coins??0;
            if(existing==0&&market.Credits.Length>=16 || existing>balance.CoinLimit-listing.Price)return CraftOutcome.MaterialFull;
            quote=quote with {OutputName=_progressionCatalog.Items[listing.Item.DefinitionId].Name,CoinChange=-listing.Price};
        }
        else{if(listing.Seller!=owner)return CraftOutcome.Unavailable;quote=quote with {OutputName=_progressionCatalog.Items[listing.Item.DefinitionId].Name};}
        return Inventory!.CanReceiveEconomy(id,quote.CoinChange,listing.Item);
    }
    private static bool SameQuote(EconomyQuote a,EconomyQuote b)=>a.Command==b.Command&&a.Chance==b.Chance&&a.DurabilityLoss==b.DurabilityLoss&&a.AttackGain==b.AttackGain&&a.OutputName==b.OutputName&&a.CoinChange==b.CoinChange&&a.Costs.SequenceEqual(b.Costs);
    private void SimulateEconomy()
    {
        foreach(var (connection,command) in _economyPending)
        {
            var player=_playersByConnection[connection];var id=player.EntityId;var outcome=Inventory!.CheckEconomyOperation(id,command,out var effect);
            if(outcome==CraftOutcome.Accepted)
            {
                var actor=Combat!.Get(id);
                if(_nodeAudit.Count>=128)outcome=CraftOutcome.RateLimited;
                else if(actor.Health<=0)outcome=CraftOutcome.InvalidState;
                else if(actor.IsCasting||player.Motion.IsMoving||player.Motion.IsDashing||Abilities!.HasActiveEffects(id))outcome=CraftOutcome.Busy;
                else if(Vector3.DistanceSquared(player.Foot, GroundFoot(_crafting!.StationPosition))>_crafting.InteractionRange*_crafting.InteractionRange)outcome=CraftOutcome.TooFar;
                else if(!Navigation.ClearAttack(player.Foot, GroundFoot(_crafting.StationPosition)))outcome=CraftOutcome.Blocked;
                else
                {
                    outcome=Inventory.PrepareEconomy(id,command,out var quote);
                    SavedListing? listing=null;var credit=0;
                    if(outcome==CraftOutcome.Accepted)outcome=PrepareMarket(connection,command,ref quote,out listing,out credit);
                    if(outcome==CraftOutcome.Accepted)
                    {
                        if(command.QuoteId==0)
                        {
                            if(_economyQuotes.TryGetValue(connection,out var old)&&old.Expires>Combat.Time&&SameQuote(old.Quote with {Command=command},quote))_economyQuoteResults[id]=old.Quote with {ServerTick=Tick,ValidSeconds=(float)(old.Expires-Combat.Time)};
                            else{quote=quote with {Command=command with {QuoteId=(ulong)Random.Shared.NextInt64(1,long.MaxValue)},ServerTick=Tick};_economyQuotes[connection]=(quote,Combat.Time+15);_economyQuoteResults[id]=quote;}
                            continue;
                        }
                        if(!_economyQuotes.TryGetValue(connection,out var saved)||saved.Expires<=Combat.Time||!SameQuote(saved.Quote,quote))outcome=CraftOutcome.InvalidOperation;
                        else
                        {
                            var market=_nodeState.Market??SavedMarket.Empty;var owner=EconomyOwner(connection);var next=market;
                            if(command.Action==EconomyAction.List)next=market with {NextListing=market.NextListing+1,Listings=[..market.Listings,new(market.NextListing,owner,Inventory.EconomyItemSaved(id,command.ItemHandle)!,command.Price)]};
                            else if(command.Action is EconomyAction.Buy or EconomyAction.Cancel)
                            {
                                next=market with {Listings=market.Listings.Where(l=>l.Id!=listing!.Id).ToArray()};
                                if(command.Action==EconomyAction.Buy){var amounts=market.Credits.ToDictionary(c=>c.Seller,c=>c.Coins);amounts[listing!.Seller]=amounts.GetValueOrDefault(listing.Seller)+listing.Price;next=next with {Credits=amounts.OrderBy(a=>a.Key).Select(a=>new SavedMarketCredit(a.Key,a.Value)).ToArray()};}
                            }
                            else if(command.Action==EconomyAction.Claim)next=market with {Credits=market.Credits.Where(c=>c.Seller!=owner).ToArray()};
                            next.Validate();
                            // Market and death loot share the bounded world document. Reject before moving ownership.
                            if(System.Text.Encoding.UTF8.GetByteCount((_nodeState with {Market=next}).Serialize())>8192)
                            { _economyResults[id]=new(command.Operation,Tick,CraftOutcome.InventoryFull,EconomyEffect.None); continue; }
                            effect=Inventory.ApplyEconomy(id,command,quote,command.Action is EconomyAction.Buy or EconomyAction.Cancel?listing!.Item:null);
                            if(command.Action is EconomyAction.List or EconomyAction.Buy or EconomyAction.Cancel or EconomyAction.Claim){_nodeState=_nodeState with {Market=next};MarketDirty=true;}
                            MarkPersistent(id);Audit(_persistentActors.GetValueOrDefault(connection,"runtime-"+connection),"Economy",$"{command.Action}, operation {command.Operation}, effect {effect}");_economyQuotes.Remove(connection);
                        }
                    }
                }
            }
            _economyResults[id]=new(command.Operation,Tick,outcome,effect);
        }
        _economyPending.Clear();
    }
    private void SimulateResourceRefill()
    {
        if(!HasEconomy)return;var now=ResourceClock();
        for(var index=0;index<_resourceStock.Length;index++)
        {
            var s=_resourceStock[index];if(s.Remaining!=0)continue;
            if(_nodeAudit.Count>=128)break;
            if(s.RefillAt==0){var next=(SavedResourceStock[])_resourceStock.Clone();next[index]=s with {RefillAt=checked(now+_progressionCatalog!.Economy!.RefillSeconds)};_resourceStock=next;_nodeState=_nodeState with {Resources=next};Audit("world","ResourceTimer",$"Resource {s.Id}");}
            else if(s.RefillAt<=now){var next=(SavedResourceStock[])_resourceStock.Clone();next[index]=s with {Remaining=_crafting!.Resources.Single(n=>n.Id==s.Id).Stock,RefillAt=0};_resourceStock=next;_nodeState=_nodeState with {Resources=next};_resourceDirty.Add(s.Id);Audit("world","ResourceRefill",$"Resource {s.Id}");}
        }
    }
    public void ClearEconomyResults(){_economyQuoteResults.Clear();_economyResults.Clear();MarketDirty=false;}
    private void RemoveEconomyPlayer(int connection,NetworkEntityId id){_economyPending.Remove(connection);_economyRequestTicks.Remove(connection);_economyQuotes.Remove(connection);_economyQuoteResults.Remove(id);_economyResults.Remove(id);}
}
