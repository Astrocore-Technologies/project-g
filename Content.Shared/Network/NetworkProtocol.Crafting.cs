using LiteNetLib.Utils;
namespace Content.Shared.Network;
public static partial class NetworkProtocol
{
    private static bool CraftName(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length<=24 && System.Text.Encoding.UTF8.GetByteCount(s)<=72 && !s.Any(char.IsControl);
    private static bool Operation(ulong n) => n is > 0 and <= long.MaxValue;
    public static NetDataWriter Write(CraftCommand s)
    { if(!Operation(s.Operation) || !Enum.IsDefined(s.Action) || s.Target==0) throw new ArgumentException("Invalid craft intention."); var w=CreateWriter(NetworkMessageType.CraftCommand); w.Put(s.Operation); w.Put((byte)s.Action); w.Put(s.Target); return w; }
    public static bool TryReadCraftCommand(NetDataReader r,out CraftCommand s)
    { s=default; if(r.AvailableBytes!=11 || !r.TryGetULong(out var op) || !Operation(op) || !r.TryGetByte(out var action) || !Enum.IsDefined((CraftAction)action) || !r.TryGetUShort(out var target) || target==0) return false; s=new(op,(CraftAction)action,target); return true; }
    public static NetDataWriter Write(CraftResult s)
    { if(!Operation(s.Operation) || !Enum.IsDefined(s.Outcome)) throw new ArgumentException("Invalid craft result."); var w=CreateWriter(NetworkMessageType.CraftResult); w.Put(s.Operation); w.Put(s.ServerTick); w.Put((byte)s.Outcome); return w; }
    public static bool TryReadCraftResult(NetDataReader r,out CraftResult s)
    { s=default; if(r.AvailableBytes!=13 || !r.TryGetULong(out var op) || !Operation(op) || !r.TryGetUInt(out var tick) || !r.TryGetByte(out var outcome) || !Enum.IsDefined((CraftOutcome)outcome)) return false; s=new(op,tick,(CraftOutcome)outcome); return true; }
    private static bool CraftAmounts(IReadOnlyList<MaterialAmount>? values)
    { if(values is null || values.Count>16) return false; var ids=new HashSet<ushort>(); foreach(var v in values) if(v.Id==0 || v.Quantity is < 1 or > 999 || !ids.Add(v.Id)) return false; return true; }
    public static NetDataWriter Write(CraftState s)
    {
        if(!s.OwnerId.IsValid || s.LastOperation>long.MaxValue || !float.IsFinite(s.CooldownSeconds) || s.CooldownSeconds is < 0 or > 60 || !CraftAmounts(s.Materials)) throw new ArgumentException("Invalid craft state.");
        var w=CreateWriter(NetworkMessageType.CraftState); w.Put(s.OwnerId.Value); w.Put(s.ServerTick); w.Put(s.LastOperation); w.Put(s.CooldownSeconds); w.Put((byte)s.Materials.Count); foreach(var m in s.Materials) { w.Put(m.Id); w.Put(m.Quantity); } return w;
    }
    public static bool TryReadCraftState(NetDataReader r,out CraftState s)
    {
        s=default; if(r.AvailableBytes>89 || !r.TryGetULong(out var owner) || owner==0 || !r.TryGetUInt(out var tick) || !r.TryGetULong(out var op) || op>long.MaxValue || !r.TryGetFloat(out var cooldown) || !float.IsFinite(cooldown) || cooldown is < 0 or > 60 || !r.TryGetByte(out var count) || count>16 || r.AvailableBytes!=count*4) return false;
        var values=new MaterialAmount[count]; for(var i=0;i<count;i++) { if(!r.TryGetUShort(out var id) || !r.TryGetUShort(out var quantity)) return false; values[i]=new(id,quantity); } if(!CraftAmounts(values)) return false; s=new(new(owner),tick,op,cooldown,values); return true;
    }
    public static NetDataWriter Write(ResourceNodeState s)
    { if(s.Id==0 || s.MaterialId==0 || s.Remaining>1000 || !StarterPoint(s.Position) || !CraftName(s.Name)) throw new ArgumentException("Invalid resource."); var w=CreateWriter(NetworkMessageType.ResourceNodeState); w.Put(s.Id); w.Put(s.ServerTick); w.Put(s.Position.X); w.Put(s.Position.Y); w.Put(s.MaterialId); w.Put(s.Remaining); w.Put(s.Name); return w; }
    public static bool TryReadResourceNodeState(NetDataReader r,out ResourceNodeState s)
    { s=default; if(r.AvailableBytes>92 || !r.TryGetUShort(out var id) || id==0 || !r.TryGetUInt(out var tick) || !r.TryGetFloat(out var x) || !r.TryGetFloat(out var z) || !StarterPoint(new(x,z)) || !r.TryGetUShort(out var material) || material==0 || !r.TryGetUShort(out var remaining) || remaining>1000 || !r.TryGetString(out var name) || !CraftName(name) || r.AvailableBytes!=0) return false; s=new(id,tick,new(x,z),material,remaining,name); return true; }
    public static NetDataWriter Write(ResourceNodeDespawn s)
    { if(s.Id==0) throw new ArgumentException("Invalid resource identity."); var w=CreateWriter(NetworkMessageType.ResourceNodeDespawn); w.Put(s.Id); w.Put(s.ServerTick); return w; }
    public static bool TryReadResourceNodeDespawn(NetDataReader r,out ResourceNodeDespawn s)
    { s=default; if(r.AvailableBytes!=6 || !r.TryGetUShort(out var id) || id==0 || !r.TryGetUInt(out var tick)) return false; s=new(id,tick); return true; }
    private static bool ValidRecipe(CraftRecipeState s)
    {
        if(s.Id==0 || !CraftName(s.Name) || !CraftName(s.OutputName) || !CraftName(s.StationName) || !StarterPoint(s.StationPosition) || !float.IsFinite(s.InteractionRange) || s.InteractionRange is < 0.5f or > 3 || s.Costs is null || s.Costs.Count is < 1 or > 4) return false;
        var ids=new HashSet<ushort>(); foreach(var c in s.Costs) if(c.Id==0 || c.Quantity is < 1 or > 100 || !CraftName(c.Name) || !ids.Add(c.Id)) return false; return true;
    }
    public static NetDataWriter Write(CraftRecipeState s)
    {
        if(!ValidRecipe(s)) throw new ArgumentException("Invalid public recipe."); var w=CreateWriter(NetworkMessageType.CraftRecipeState); w.Put(s.Id); w.Put(s.Name); w.Put(s.OutputName); w.Put(s.StationPosition.X); w.Put(s.StationPosition.Y); w.Put(s.StationName); w.Put(s.InteractionRange); w.Put((byte)s.Costs.Count); foreach(var c in s.Costs) { w.Put(c.Id); w.Put(c.Quantity); w.Put(c.Name); } return w;
    }
    public static bool TryReadCraftRecipeState(NetDataReader r,out CraftRecipeState s)
    {
        s=default; if(r.AvailableBytes>549 || !r.TryGetUShort(out var id) || !r.TryGetString(out var name) || !r.TryGetString(out var output) || !r.TryGetFloat(out var x) || !r.TryGetFloat(out var z) || !r.TryGetString(out var station) || !r.TryGetFloat(out var range) || !r.TryGetByte(out var count) || count is < 1 or > 4) return false;
        var costs=new CraftIngredient[count]; for(var i=0;i<count;i++) { if(!r.TryGetUShort(out var material) || !r.TryGetUShort(out var quantity) || !r.TryGetString(out var title)) return false; costs[i]=new(material,quantity,title); }
        var value=new CraftRecipeState(id,name,output,new(x,z),station,range,costs); if(r.AvailableBytes!=0 || !ValidRecipe(value)) return false; s=value; return true;
    }
}
