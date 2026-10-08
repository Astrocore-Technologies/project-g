namespace Content.Shared.Network;
public enum TradeAction:byte { Invite=1,Offer=2,Accept=3,Cancel=4 }
public enum TradePhase:byte { Invited=1,Negotiating=2,Completed=3,Cancelled=4 }
public readonly record struct TradeCommand(uint Sequence,TradeAction Action,ulong SessionId,NetworkEntityId PartnerId,uint Revision,IReadOnlyList<ulong> Items,IReadOnlyList<MaterialAmount> Materials);
public readonly record struct TradeItem(ulong Handle,string Name,EquipmentSlot Slot,double Attack,double Defense,double Health,ushort Current,ushort Maximum);
public readonly record struct TradeState(NetworkEntityId OwnerId,uint ServerTick,ulong SessionId,NetworkEntityId PartnerId,uint Revision,TradePhase Phase,bool OwnAccepted,bool PartnerAccepted,IReadOnlyList<TradeItem> OwnItems,IReadOnlyList<MaterialAmount> OwnMaterials,IReadOnlyList<TradeItem> PartnerItems,IReadOnlyList<MaterialAmount> PartnerMaterials);
public readonly record struct TradeResult(uint Sequence,uint ServerTick,CraftOutcome Outcome);
