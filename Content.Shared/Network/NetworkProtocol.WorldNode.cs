using System.Text;
using LiteNetLib.Utils;
namespace Content.Shared.Network;
public static partial class NetworkProtocol
{
    public static NetDataWriter Write(WorldNodeCommand c)
    {
        if(c.Sequence==0 || !Enum.IsDefined(c.Action)) throw new ArgumentException("Invalid world intention.");
        var w=CreateWriter(NetworkMessageType.WorldNodeCommand); w.Put(c.Sequence); w.Put((byte)c.Action); return w;
    }
    public static bool TryReadWorldNodeCommand(NetDataReader r,out WorldNodeCommand c)
    {
        c=default;
        if(r.AvailableBytes!=5 || !r.TryGetUInt(out var seq) || seq==0 || !r.TryGetByte(out var action) || !Enum.IsDefined((WorldNodeAction)action)) return false;
        c=new(seq,(WorldNodeAction)action); return true;
    }
    private static bool WorldText(string s,int chars,int bytes) => !string.IsNullOrWhiteSpace(s) && s.Length<=chars && Encoding.UTF8.GetByteCount(s)<=bytes && !s.Any(char.IsControl);
    public static NetDataWriter Write(WorldNodeState s)
    {
        if(s.Revision==0 || s.Revision>long.MaxValue || !float.IsFinite(s.Position.X) || !float.IsFinite(s.Position.Y) || s.Consequences>3 || !WorldText(s.KeeperName,32,96) || !WorldText(s.KeeperLine,100,300) || !WorldText(s.Rumor,100,300)) throw new ArgumentException("Invalid public node.");
        var w=CreateWriter(NetworkMessageType.WorldNodeState); w.Put(s.Revision); w.Put(s.ServerTick); w.Put(s.Position.X); w.Put(s.Position.Y); w.Put(s.Consequences); w.Put(s.KeeperName); w.Put(s.KeeperLine); w.Put(s.Rumor); return w;
    }
    public static bool TryReadWorldNodeState(NetDataReader r,out WorldNodeState s)
    {
        s=default;
        if(r.AvailableBytes>723 || !r.TryGetULong(out var rev) || rev==0 || rev>long.MaxValue || !r.TryGetUInt(out var tick) || !r.TryGetFloat(out var x) || !float.IsFinite(x) || !r.TryGetFloat(out var z) || !float.IsFinite(z) || !r.TryGetByte(out var flags) || flags>3 || !r.TryGetString(out var name) || !WorldText(name,32,96) || !r.TryGetString(out var line) || !WorldText(line,100,300) || !r.TryGetString(out var rumor) || !WorldText(rumor,100,300) || r.AvailableBytes!=0) return false;
        s=new(rev,tick,new(x,z),flags,name,line,rumor); return true;
    }
    public static NetDataWriter Write(WorldNodeResult s)
    {
        if(s.Sequence==0 || !Enum.IsDefined(s.Outcome)) throw new ArgumentException("Invalid node result.");
        var w=CreateWriter(NetworkMessageType.WorldNodeResult); w.Put(s.Sequence); w.Put(s.ServerTick); w.Put((byte)s.Outcome); return w;
    }
    public static bool TryReadWorldNodeResult(NetDataReader r,out WorldNodeResult s)
    {
        s=default;
        if(r.AvailableBytes!=9 || !r.TryGetUInt(out var seq) || seq==0 || !r.TryGetUInt(out var tick) || !r.TryGetByte(out var outcome) || !Enum.IsDefined((WorldNodeOutcome)outcome)) return false;
        s=new(seq,tick,(WorldNodeOutcome)outcome); return true;
    }
}
