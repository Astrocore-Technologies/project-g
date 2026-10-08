namespace Content.Shared.Network;
public readonly record struct RepairCommand(ulong Operation,ulong ItemHandle,ulong ItemRevision,ulong QuoteId);
public readonly record struct RepairQuote(ulong Operation,ulong ItemHandle,ulong ItemRevision,ulong QuoteId,uint ServerTick,ushort MaterialId,ushort Quantity,float ValidSeconds);
public readonly record struct RepairResult(ulong Operation,uint ServerTick,CraftOutcome Outcome);
public readonly record struct ItemConditionEntry(ulong Handle,ushort Current,ushort Maximum,ulong Revision);
public readonly record struct ItemConditionState(NetworkEntityId OwnerId,uint ServerTick,ulong LastOperation,IReadOnlyList<ItemConditionEntry> Items);
