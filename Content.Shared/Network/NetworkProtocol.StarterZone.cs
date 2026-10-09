using System.Text;
using LiteNetLib.Utils;
namespace Content.Shared.Network;
public static partial class NetworkProtocol
{
    private static bool StarterName(string s) => !string.IsNullOrWhiteSpace(s) && s.Length<=32 && Encoding.UTF8.GetByteCount(s)<=96 && !s.Any(char.IsControl);
    private static bool StarterPoint(System.Numerics.Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
    public static NetDataWriter Write(StarterZoneState s)
    {
        if(!StarterName(s.RegionName) || !StarterName(s.TownName) || !StarterName(s.GuideName) || !StarterPoint(s.TownPosition) || !StarterPoint(s.GuidePosition)) throw new ArgumentException("Invalid starter zone.");
        var w=CreateWriter(NetworkMessageType.StarterZoneState); w.Put(s.RegionName); w.Put(s.TownName); w.Put(s.TownPosition.X); w.Put(s.TownPosition.Y); w.Put(s.GuideName); w.Put(s.GuidePosition.X); w.Put(s.GuidePosition.Y); return w;
    }
    public static bool TryReadStarterZoneState(NetDataReader r,out StarterZoneState s)
    {
        s=default;
        if(r.AvailableBytes>310 || !r.TryGetString(out var region) || !StarterName(region) || !r.TryGetString(out var town) || !StarterName(town) || !r.TryGetFloat(out var x) || !r.TryGetFloat(out var z) || !r.TryGetString(out var guide) || !StarterName(guide) || !r.TryGetFloat(out var gx) || !r.TryGetFloat(out var gz) || r.AvailableBytes!=0 || !StarterPoint(new(x,z)) || !StarterPoint(new(gx,gz))) return false;
        s=new(region,town,new(x,z),guide,new(gx,gz)); return true;
    }
    private static bool ValidExploration(ExplorationState s)
    {
        var count=(long)s.Width*s.Height;
        if(!s.OwnerId.IsValid || s.Width==0 || s.Height==0 || count>NetworkConstants.MaxNavigationCells || s.Tutorial>127 || s.Cells is null || s.Cells.Length!=(count+7)/8 || s.Places is null || s.Places.Count>2 || ((count%8)!=0 && s.Cells[^1]>>(int)(count%8)!=0)) return false;
        ushort mask=0;
        foreach(var p in s.Places)
        {
            if(p.Id is < 1 or > 2 || (mask&(1<<p.Id))!=0 || !StarterName(p.Name) || !StarterPoint(p.Position)) return false;
            mask|=(ushort)(1<<p.Id);
        }
        return true;
    }
    public static NetDataWriter Write(ExplorationState s)
    {
        if(!ValidExploration(s)) throw new ArgumentException("Invalid revealed exploration.");
        var w=CreateWriter(NetworkMessageType.ExplorationState); w.Put(s.OwnerId.Value); w.Put(s.ServerTick); w.Put(s.Width); w.Put(s.Height); w.Put(s.Tutorial); w.Put((ushort)s.Cells.Length); w.Put(s.Cells); w.Put((byte)s.Places.Count);
        foreach(var p in s.Places) { w.Put(p.Id); w.Put(p.Position.X); w.Put(p.Position.Y); w.Put(p.Name); } return w;
    }
    public static bool TryReadExplorationState(NetDataReader r,out ExplorationState s)
    {
        s=default;
        if(r.AvailableBytes>NetworkConstants.MaxGamePacketBytes-RegionEnvelopeBytes-sizeof(ushort) || !r.TryGetULong(out var owner) || owner==0 || !r.TryGetUInt(out var tick) || !r.TryGetUShort(out var width) || !r.TryGetUShort(out var height) || width==0 || height==0 || (long)width*height>NetworkConstants.MaxNavigationCells || !r.TryGetByte(out var tutorial) || tutorial>127 || !r.TryGetUShort(out var bytes) || bytes!=((int)width*height+7)/8 || r.AvailableBytes<bytes+1) return false;
        var cells=new byte[bytes]; for(var i=0;i<cells.Length;i++) if(!r.TryGetByte(out cells[i])) return false;
        if(!r.TryGetByte(out var places) || places>2) return false;
        var points=new RevealedPlace[places];
        for(var i=0;i<points.Length;i++)
        { if(!r.TryGetUShort(out var id) || !r.TryGetFloat(out var x) || !r.TryGetFloat(out var z) || !r.TryGetString(out var name)) return false; points[i]=new(id,name,new(x,z)); }
        var value=new ExplorationState(new(owner),tick,width,height,tutorial,cells,points);
        if(r.AvailableBytes!=0 || !ValidExploration(value)) return false; s=value; return true;
    }
}
