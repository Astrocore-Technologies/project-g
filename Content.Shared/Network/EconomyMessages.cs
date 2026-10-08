namespace Content.Shared.Network;
public enum EconomyAction:byte { Evolve=1,Enhance=2,SellOre=3,List=4,Buy=5,Cancel=6,Claim=7 }
public enum EconomyEffect:byte { None,Evolved,Enhanced,Failed,Destroyed,SoldOre,Listed,Bought,Cancelled,Claimed }
public readonly record struct EconomyCommand(ulong Operation,EconomyAction Action,ulong ItemHandle,ulong ItemRevision,ulong ListingId,ushort Quantity,int Price,ulong QuoteId);
public readonly record struct EconomyQuote(EconomyCommand Command,uint ServerTick,byte Chance,ushort DurabilityLoss,string OutputName,double AttackGain,int CoinChange,float ValidSeconds,IReadOnlyList<MaterialAmount> Costs);
public readonly record struct EconomyResult(ulong Operation,uint ServerTick,CraftOutcome Outcome,EconomyEffect Effect);
public readonly record struct EconomyItem(ulong Handle,ulong Revision,byte Enhancement);
public readonly record struct EconomyState(NetworkEntityId OwnerId,uint ServerTick,ulong LastOperation,int Coins,EconomyEffect LastEffect,IReadOnlyList<EconomyItem> Items);
public readonly record struct MarketListing(ulong Id,int Price,bool Own,TradeItem Item);
public readonly record struct MarketState(NetworkEntityId OwnerId,uint ServerTick,int Credit,IReadOnlyList<MarketListing> Listings);
