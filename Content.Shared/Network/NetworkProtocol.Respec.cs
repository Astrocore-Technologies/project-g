using LiteNetLib.Utils;
namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static bool ValidRespecCommand(RespecCommand c)=>c.Sequence!=0&&c.NpcId.IsValid&&Enum.IsDefined(c.Action)&&
        (c.Action==RespecAction.Quote?c.QuoteId==0&&c.Operation==0:c.QuoteId is >0 and <=long.MaxValue&&c.Operation is >0 and <=int.MaxValue);
    public static NetDataWriter Write(RespecCommand c)
    {
        if(!ValidRespecCommand(c))throw new ArgumentException("Invalid reset command.");
        var w=CreateWriter(NetworkMessageType.RespecCommand);w.Put(c.Sequence);w.Put((byte)c.Action);w.Put(c.NpcId.Value);w.Put(c.QuoteId);w.Put(c.Operation);return w;
    }
    public static bool TryReadRespecCommand(NetDataReader r,out RespecCommand value)
    {
        value=default;if(r.AvailableBytes!=29||!r.TryGetUInt(out var seq)||!r.TryGetByte(out var action)||!r.TryGetULong(out var npc)||
            !r.TryGetULong(out var quote)||!r.TryGetULong(out var operation))return false;
        var c=new RespecCommand(seq,(RespecAction)action,new(npc),quote,operation);if(!ValidRespecCommand(c))return false;value=c;return true;
    }
    private static bool ValidRespecQuote(RespecQuote q)=>q.Sequence!=0&&q.NpcId.IsValid&&q.QuoteId is >0 and <=long.MaxValue&&q.Operation is >0 and <=int.MaxValue&&
        q.Price is >=0 and <=1000000&&q.Coins is >=0 and <=1000000&&q.ReturnedPoints is >0 and <=100000&&q.AvailableAfter>=q.ReturnedPoints&&q.AvailableAfter<=100000&&
        float.IsFinite(q.ValidSeconds)&&q.ValidSeconds is >0 and <=60&&q.FirstFree==(q.Operation==1)&&q.FirstFree==(q.Price==0)&&
        q.PrimaryBefore is {Length:7}&&q.PrimaryAfter is {Length:7}&&q.PrimaryBefore.All(v=>double.IsFinite(v)&&v>=0)&&q.PrimaryAfter.All(v=>double.IsFinite(v)&&v>=0)&&
        ValidDisplayStats(q.Current)&&ValidDisplayStats(q.Projected);
    public static NetDataWriter Write(RespecQuote q)
    {
        if(!ValidRespecQuote(q))throw new ArgumentException("Invalid reset quote.");
        var w=CreateWriter(NetworkMessageType.RespecQuote);w.Put(q.Sequence);w.Put(q.ServerTick);w.Put(q.NpcId.Value);w.Put(q.QuoteId);w.Put(q.Operation);
        w.Put(q.Price);w.Put(q.Coins);w.Put(q.ReturnedPoints);w.Put(q.AvailableAfter);w.Put(q.ValidSeconds);w.Put(q.FirstFree);
        foreach(var values in new[]{q.PrimaryBefore,q.PrimaryAfter,q.Current,q.Projected})foreach(var v in values)w.Put(v);return w;
    }
    public static bool TryReadRespecQuote(NetDataReader r,out RespecQuote value)
    {
        value=default;if(r.AvailableBytes!=53+14*8+NetworkConstants.CharacterStatCount*16||!r.TryGetUInt(out var seq)||!r.TryGetUInt(out var tick)||
            !r.TryGetULong(out var npc)||!r.TryGetULong(out var quote)||!r.TryGetULong(out var operation)||!r.TryGetInt(out var price)||!r.TryGetInt(out var coins)||
            !r.TryGetInt(out var returned)||!r.TryGetInt(out var available)||!r.TryGetFloat(out var seconds)||!r.TryGetByte(out var free)||free>1)return false;
        var primary=new double[7];var after=new double[7];var current=new double[NetworkConstants.CharacterStatCount];var projected=new double[NetworkConstants.CharacterStatCount];
        foreach(var values in new[]{primary,after,current,projected})for(var i=0;i<values.Length;i++)if(!r.TryGetDouble(out values[i]))return false;
        var q=new RespecQuote(seq,tick,new(npc),quote,operation,price,coins,returned,available,seconds,free==1,primary,after,current,projected);
        if(!ValidRespecQuote(q))return false;value=q;return true;
    }
    private static bool ValidRespecResult(RespecResult r)=>r.Sequence!=0&&r.Operation<=int.MaxValue&&Enum.IsDefined(r.Outcome)&&
        r.PaidCoins is >=0 and <=1000000&&r.ReturnedPoints is >=0 and <=100000&&
        (r.Outcome is RespecOutcome.Accepted or RespecOutcome.AlreadyProcessed?r.Operation>0&&r.ReturnedPoints>0:r.PaidCoins==0&&r.ReturnedPoints==0);
    public static NetDataWriter Write(RespecResult r)
    {
        if(!ValidRespecResult(r))throw new ArgumentException("Invalid reset result.");
        var w=CreateWriter(NetworkMessageType.RespecResult);w.Put(r.Sequence);w.Put(r.ServerTick);w.Put(r.Operation);w.Put((byte)r.Outcome);w.Put(r.PaidCoins);w.Put(r.ReturnedPoints);return w;
    }
    public static bool TryReadRespecResult(NetDataReader r,out RespecResult value)
    {
        value=default;if(r.AvailableBytes!=25||!r.TryGetUInt(out var seq)||!r.TryGetUInt(out var tick)||!r.TryGetULong(out var operation)||
            !r.TryGetByte(out var outcome)||!r.TryGetInt(out var paid)||!r.TryGetInt(out var returned))return false;
        var result=new RespecResult(seq,tick,operation,(RespecOutcome)outcome,paid,returned);if(!ValidRespecResult(result))return false;value=result;return true;
    }
}
