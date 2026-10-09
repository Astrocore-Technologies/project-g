using System.Text;
using LiteNetLib.Utils;
namespace Content.Shared.Network;
public static partial class NetworkProtocol
{
    private static bool ValidProfessionCommand(ProfessionCommand c) => c.Sequence != 0 && Enum.IsDefined(c.Action) &&
        (c.Action == ProfessionAction.Cancel ? c.ProfessionId == 0 && c.Confirmation == 0 : c.ProfessionId != 0 &&
        (c.Action == ProfessionAction.Prepare ? c.Confirmation == 0 : c.Confirmation != 0));
    public static NetDataWriter Write(ProfessionCommand c)
    {
        if (!ValidProfessionCommand(c)) throw new ArgumentException("Invalid profession intention.");
        var w=CreateWriter(NetworkMessageType.ProfessionCommand); w.Put(c.Sequence); w.Put((byte)c.Action); w.Put(c.ProfessionId); w.Put(c.Confirmation); return w;
    }
    public static bool TryReadProfessionCommand(NetDataReader r,out ProfessionCommand c)
    {
        c=default;
        if (r.AvailableBytes != 11 || !r.TryGetUInt(out var seq) || !r.TryGetByte(out var action) || !r.TryGetUShort(out var id) || !r.TryGetUInt(out var token)) return false;
        var value=new ProfessionCommand(seq,(ProfessionAction)action,id,token); if (!ValidProfessionCommand(value)) return false; c=value; return true;
    }
    private static bool ValidProfessionName(ushort id,string name) => name is not null && (id == 0 ? name.Length == 0 : name.Length is > 0 and <= 24 && Encoding.UTF8.GetByteCount(name) <= 72);
    public static NetDataWriter Write(ProfessionState s)
    {
        if (!s.OwnerId.IsValid || !ValidProfessionName(s.ActiveId,s.ActiveName) || !ValidProfessionName(s.OfferedId,s.OfferedName) || (s.ActiveId != 0 && s.ActiveId == s.OfferedId) || !ValidSwordTraining(s.TrainingDamage,s.TrainingRequired)) throw new ArgumentException("Invalid revealed profession state.");
        var w=CreateWriter(NetworkMessageType.ProfessionState); w.Put(s.OwnerId.Value); w.Put(s.ServerTick); w.Put(s.ActiveId); w.Put(s.ActiveName); w.Put(s.OfferedId); w.Put(s.OfferedName); w.Put(s.TrainingDamage); w.Put(s.TrainingRequired); return w;
    }
    public static bool TryReadProfessionState(NetDataReader r,out ProfessionState s)
    {
        s=default;
        if (r.AvailableBytes > 186 || !r.TryGetULong(out var owner) || owner == 0 || !r.TryGetUInt(out var tick) || !r.TryGetUShort(out var active) || !r.TryGetString(out var activeName) || !r.TryGetUShort(out var offer) || !r.TryGetString(out var offerName) ||
            !r.TryGetDouble(out var damage) || !r.TryGetDouble(out var required) || !ValidSwordTraining(damage,required) || r.AvailableBytes != 0 ||
            !ValidProfessionName(active,activeName) || !ValidProfessionName(offer,offerName) || (active != 0 && active == offer)) return false;
        s=new(new(owner),tick,active,activeName,offer,offerName,damage,required); return true;
    }
    private static bool ValidSwordTraining(double damage,double required) => double.IsFinite(damage) && double.IsFinite(required) && damage >= 0 && damage <= required && required is >= 0 and <= 100000;
    public static NetDataWriter Write(ProfessionResult s)
    {
        if (s.Sequence == 0 || !Enum.IsDefined(s.Outcome) || (s.Outcome == ProfessionOutcome.Prepared ? s.Confirmation == 0 : s.Confirmation != 0)) throw new ArgumentException("Invalid profession result.");
        var w=CreateWriter(NetworkMessageType.ProfessionResult); w.Put(s.Sequence); w.Put(s.ServerTick); w.Put((byte)s.Outcome); w.Put(s.Confirmation); return w;
    }
    public static bool TryReadProfessionResult(NetDataReader r,out ProfessionResult s)
    {
        s=default;
        if (r.AvailableBytes != 13 || !r.TryGetUInt(out var seq) || seq == 0 || !r.TryGetUInt(out var tick) || !r.TryGetByte(out var outcome) || !Enum.IsDefined((ProfessionOutcome)outcome) || !r.TryGetUInt(out var token) ||
            ((ProfessionOutcome)outcome == ProfessionOutcome.Prepared ? token == 0 : token != 0)) return false;
        s=new(seq,tick,(ProfessionOutcome)outcome,token); return true;
    }
}
