using LiteNetLib.Utils;
namespace Content.Shared.Network;
public static partial class NetworkProtocol
{
    private static bool RepairCommandValid(RepairCommand v) => Operation(v.Operation) && v.ItemHandle!=0 && Operation(v.ItemRevision) && v.QuoteId<=long.MaxValue;
    public static NetDataWriter Write(RepairCommand v)
    { if(!RepairCommandValid(v)) throw new ArgumentException("Invalid repair intention."); var w=CreateWriter(NetworkMessageType.RepairCommand); w.Put(v.Operation); w.Put(v.ItemHandle); w.Put(v.ItemRevision); w.Put(v.QuoteId); return w; }
    public static bool TryReadRepairCommand(NetDataReader r,out RepairCommand v)
    { v=default; if(r.AvailableBytes!=32 || !r.TryGetULong(out var op) || !r.TryGetULong(out var handle) || !r.TryGetULong(out var revision) || !r.TryGetULong(out var quote)) return false; var value=new RepairCommand(op,handle,revision,quote); if(!RepairCommandValid(value)) return false; v=value; return true; }
    private static bool RepairQuoteValid(RepairQuote v) => RepairCommandValid(new(v.Operation,v.ItemHandle,v.ItemRevision,v.QuoteId)) && v.QuoteId>0 && v.MaterialId>0 && v.Quantity is >0 and <=1000 && float.IsFinite(v.ValidSeconds) && v.ValidSeconds is >0 and <=30;
    public static NetDataWriter Write(RepairQuote v)
    { if(!RepairQuoteValid(v)) throw new ArgumentException("Invalid repair quote."); var w=CreateWriter(NetworkMessageType.RepairQuote); w.Put(v.Operation); w.Put(v.ItemHandle); w.Put(v.ItemRevision); w.Put(v.QuoteId); w.Put(v.ServerTick); w.Put(v.MaterialId); w.Put(v.Quantity); w.Put(v.ValidSeconds); return w; }
    public static bool TryReadRepairQuote(NetDataReader r,out RepairQuote v)
    { v=default; if(r.AvailableBytes!=44 || !r.TryGetULong(out var op) || !r.TryGetULong(out var handle) || !r.TryGetULong(out var revision) || !r.TryGetULong(out var quote) || !r.TryGetUInt(out var tick) || !r.TryGetUShort(out var material) || !r.TryGetUShort(out var quantity) || !r.TryGetFloat(out var seconds)) return false; var value=new RepairQuote(op,handle,revision,quote,tick,material,quantity,seconds); if(!RepairQuoteValid(value)) return false; v=value; return true; }
    public static NetDataWriter Write(RepairResult v)
    { if(!Operation(v.Operation) || !Enum.IsDefined(v.Outcome)) throw new ArgumentException("Invalid repair result."); var w=CreateWriter(NetworkMessageType.RepairResult); w.Put(v.Operation); w.Put(v.ServerTick); w.Put((byte)v.Outcome); return w; }
    public static bool TryReadRepairResult(NetDataReader r,out RepairResult v)
    { v=default; if(r.AvailableBytes!=13 || !r.TryGetULong(out var op) || !Operation(op) || !r.TryGetUInt(out var tick) || !r.TryGetByte(out var outcome) || !Enum.IsDefined((CraftOutcome)outcome)) return false; v=new(op,tick,(CraftOutcome)outcome); return true; }
    private static bool ConditionsValid(ItemConditionState v)
    { if(!v.OwnerId.IsValid || v.LastOperation>long.MaxValue || v.Items is null || v.Items.Count>NetworkConstants.MaxInventoryItems) return false; var handles=new HashSet<ulong>(); foreach(var item in v.Items) if(item.Handle==0 || !handles.Add(item.Handle) || item.Maximum is 0 or >1000 || item.Current>item.Maximum || !Operation(item.Revision)) return false; return true; }
    public static NetDataWriter Write(ItemConditionState v)
    { if(!ConditionsValid(v)) throw new ArgumentException("Invalid conditions."); var w=CreateWriter(NetworkMessageType.ItemConditionState); w.Put(v.OwnerId.Value); w.Put(v.ServerTick); w.Put(v.LastOperation); w.Put((byte)v.Items.Count); foreach(var item in v.Items) { w.Put(item.Handle); w.Put(item.Current); w.Put(item.Maximum); w.Put(item.Revision); } return w; }
    public static bool TryReadItemConditionState(NetDataReader r,out ItemConditionState v)
    { v=default; if(r.AvailableBytes>181 || !r.TryGetULong(out var owner) || !r.TryGetUInt(out var tick) || !r.TryGetULong(out var op) || !r.TryGetByte(out var count) || count>NetworkConstants.MaxInventoryItems || r.AvailableBytes!=count*20) return false; var items=new ItemConditionEntry[count]; for(var i=0;i<count;i++) { if(!r.TryGetULong(out var handle) || !r.TryGetUShort(out var current) || !r.TryGetUShort(out var maximum) || !r.TryGetULong(out var revision)) return false; items[i]=new(handle,current,maximum,revision); } var value=new ItemConditionState(new(owner),tick,op,items); if(!ConditionsValid(value)) return false; v=value; return true; }
}
