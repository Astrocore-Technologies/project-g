using LiteNetLib.Utils;
namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(TavernCommand value)
    {
        if(value.Sequence==0||!value.NpcId.IsValid)throw new ArgumentException("Invalid tavern command.");
        var w=CreateWriter(NetworkMessageType.TavernCommand);w.Put(value.Sequence);w.Put(value.NpcId.Value);return w;
    }
    public static bool TryReadTavernCommand(NetDataReader r,out TavernCommand value)
    {
        value=default;if(r.AvailableBytes!=12||!r.TryGetUInt(out var sequence)||sequence==0||!r.TryGetULong(out var npc)||npc==0)return false;
        value=new(sequence,new(npc));return true;
    }
    public static NetDataWriter Write(TavernResult value)
    {
        if(value.Sequence==0||!Enum.IsDefined(value.Outcome))throw new ArgumentException("Invalid tavern result.");
        var w=CreateWriter(NetworkMessageType.TavernResult);w.Put(value.Sequence);w.Put(value.ServerTick);w.Put((byte)value.Outcome);return w;
    }
    public static bool TryReadTavernResult(NetDataReader r,out TavernResult value)
    {
        value=default;if(r.AvailableBytes!=9||!r.TryGetUInt(out var sequence)||sequence==0||!r.TryGetUInt(out var tick)||
            !r.TryGetByte(out var outcome)||!Enum.IsDefined((TavernOutcome)outcome))return false;
        value=new(sequence,tick,(TavernOutcome)outcome);return true;
    }
    private static bool ValidTavernState(TavernState value)=>value.OwnerId.IsValid&&value.DeathId<=long.MaxValue&&value.ExperienceLost>=0&&
        (value.DeathId!=0||value.ExperienceLost==0&&!value.PvpDeath)&&
        (value.TavernId==0?value.TavernName==""&&value.RegionId=="":WorldText(value.TavernName,32,96)&&
            value.RegionId is {Length:>0 and <=64}&&value.RegionId.All(c=>c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'));
    public static NetDataWriter Write(TavernState value)
    {
        if(!ValidTavernState(value))throw new ArgumentException("Invalid tavern state.");
        var w=CreateWriter(NetworkMessageType.TavernState);w.Put(value.OwnerId.Value);w.Put(value.ServerTick);w.Put(value.TavernId);
        w.Put(value.TavernName);w.Put(value.RegionId);w.Put(value.DeathId);w.Put(value.ExperienceLost);w.Put(value.PvpDeath);return w;
    }
    public static bool TryReadTavernState(NetDataReader r,out TavernState value)
    {
        value=default;if(r.AvailableBytes>256||!r.TryGetULong(out var owner)||!r.TryGetUInt(out var tick)||!r.TryGetUShort(out var tavern)||
            !r.TryGetString(out var name)||!r.TryGetString(out var region)||!r.TryGetULong(out var death)||!r.TryGetInt(out var lost)||
            !r.TryGetByte(out var pvp)||pvp>1||r.AvailableBytes!=0)return false;
        var candidate=new TavernState(new(owner),tick,tavern,name,region,death,lost,pvp==1);
        if(!ValidTavernState(candidate))return false;value=candidate;return true;
    }
}
