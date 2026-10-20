using LiteNetLib.Utils;
namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static bool ValidActivityCommand(ActivityCommand value)=>value.Sequence!=0&&Enum.IsDefined(value.Action)&&value.NpcId.IsValid&&value.Order is >0 and <=long.MaxValue;
    public static NetDataWriter Write(ActivityCommand value)
    {
        if(!ValidActivityCommand(value))throw new ArgumentException("Invalid activity command.");
        var w=CreateWriter(NetworkMessageType.ActivityCommand);w.Put(value.Sequence);w.Put((byte)value.Action);w.Put(value.NpcId.Value);w.Put(value.Order);return w;
    }
    public static bool TryReadActivityCommand(NetDataReader r,out ActivityCommand value)
    {
        value=default;if(r.AvailableBytes!=21||!r.TryGetUInt(out var seq)||!r.TryGetByte(out var action)||!r.TryGetULong(out var npc)||!r.TryGetULong(out var order))return false;
        var next=new ActivityCommand(seq,(ActivityAction)action,new(npc),order);if(!ValidActivityCommand(next))return false;value=next;return true;
    }
    public static NetDataWriter Write(ActivityResult value)
    {
        if(value.Sequence==0||value.Order is 0 or >long.MaxValue||!Enum.IsDefined(value.Outcome))throw new ArgumentException("Invalid activity result.");
        var w=CreateWriter(NetworkMessageType.ActivityResult);w.Put(value.Sequence);w.Put(value.ServerTick);w.Put(value.Order);w.Put((byte)value.Outcome);return w;
    }
    public static bool TryReadActivityResult(NetDataReader r,out ActivityResult value)
    {
        value=default;if(r.AvailableBytes!=17||!r.TryGetUInt(out var seq)||seq==0||!r.TryGetUInt(out var tick)||!r.TryGetULong(out var order)||order is 0 or >long.MaxValue||
            !r.TryGetByte(out var result)||!Enum.IsDefined((QuestOutcome)result))return false;value=new(seq,tick,order,(QuestOutcome)result);return true;
    }
    private static bool ValidActivityState(ActivityState s)=>s.OwnerId.IsValid&&WorldText(s.Title,64,192)&&WorldText(s.Objective,180,540)&&WorldText(s.Material,24,72)&&
        s.Required is >0 and <=999&&s.Carried<=999&&s.Experience is >0 and <=1000000&&s.Order<=long.MaxValue&&Enum.IsDefined(s.Status)&&
        (s.Status==QuestStatus.Unknown?s.Order==0:s.Order>0)&&s.Giver.IsValid==s.Recipient.IsValid&&
        (!s.Giver.IsValid||s.Giver!=s.Recipient)&&s.WatchOccurrence<=long.MaxValue&&Enum.IsDefined(s.WatchPhase)&&
        (s.WatchPhase==CampWatchPhase.None?s.WatchOccurrence==0:s.WatchOccurrence>0)&&s.WatchExperience is >=0 and <=1000000;
    public static NetDataWriter Write(ActivityState s)
    {
        if(!ValidActivityState(s))throw new ArgumentException("Invalid activity state.");
        var w=CreateWriter(NetworkMessageType.ActivityState);w.Put(s.OwnerId.Value);w.Put(s.ServerTick);w.Put(s.Title);w.Put(s.Objective);w.Put(s.Material);
        w.Put(s.Required);w.Put(s.Carried);w.Put(s.Experience);w.Put(s.Order);w.Put((byte)s.Status);w.Put(s.Giver.Value);w.Put(s.Recipient.Value);
        w.Put(s.WatchOccurrence);w.Put((byte)s.WatchPhase);w.Put(s.WatchExperience);return w;
    }
    public static bool TryReadActivityState(NetDataReader r,out ActivityState value)
    {
        value=default;if(r.AvailableBytes>900||!r.TryGetULong(out var owner)||!r.TryGetUInt(out var tick)||!r.TryGetString(out var title)||!r.TryGetString(out var objective)||
            !r.TryGetString(out var material)||!r.TryGetUShort(out var required)||!r.TryGetUShort(out var carried)||!r.TryGetInt(out var xp)||!r.TryGetULong(out var order)||
            !r.TryGetByte(out var status)||!r.TryGetULong(out var giver)||!r.TryGetULong(out var recipient)||!r.TryGetULong(out var occurrence)||!r.TryGetByte(out var phase)||
            !r.TryGetInt(out var watchXp)||r.AvailableBytes!=0)return false;
        var s=new ActivityState(new(owner),tick,title,objective,material,required,carried,xp,order,(QuestStatus)status,new(giver),new(recipient),occurrence,(CampWatchPhase)phase,watchXp);
        if(!ValidActivityState(s))return false;value=s;return true;
    }
}
