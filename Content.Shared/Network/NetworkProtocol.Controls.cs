using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    private static bool ValidControlSummary(ControlSummary value)=>Enum.IsDefined(value.Kind) &&
        double.IsFinite(value.RemainingSeconds) && value.RemainingSeconds>0 &&
        double.IsFinite(value.Strength) && value.Strength>0 && value.Instances>0;

    public static NetDataWriter Write(ControlState value)
    {
        if(!value.OwnerId.IsValid || value.Controls is null || value.Controls.Length>NetworkConstants.MaxControlKinds)
            throw new ArgumentException("Invalid owner control state.");
        var writer=CreateWriter(NetworkMessageType.ControlState);
        writer.Put(value.OwnerId.Value);writer.Put(value.ServerTick);writer.Put((byte)value.Controls.Length);
        var previous=-1;
        foreach(var control in value.Controls)
        {
            if(!ValidControlSummary(control) || (int)control.Kind<=previous)throw new ArgumentException("Invalid or unordered control summary.");
            previous=(int)control.Kind;writer.Put((byte)control.Kind);writer.Put(control.RemainingSeconds);
            writer.Put(control.Strength);writer.Put(control.Instances);
        }
        return writer;
    }

    public static bool TryReadControlState(NetDataReader reader,out ControlState value)
    {
        value=default;
        if(reader.AvailableBytes<13 || !reader.TryGetULong(out var owner) || owner==0 || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetByte(out var count) || count>NetworkConstants.MaxControlKinds || reader.AvailableBytes!=count*21)return false;
        var controls=new ControlSummary[count];var previous=-1;
        for(var i=0;i<count;i++)
        {
            if(!reader.TryGetByte(out var kind) || !reader.TryGetDouble(out var remaining) || !reader.TryGetDouble(out var strength) ||
                !reader.TryGetUInt(out var instances))return false;
            var control=new ControlSummary((CrowdControlKind)kind,remaining,strength,instances);
            if(!ValidControlSummary(control) || kind<=previous)return false;
            controls[i]=control;previous=kind;
        }
        value=new(new(owner),tick,controls);return true;
    }
}
