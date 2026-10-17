using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    private static bool ValidControlFeedback(ControlFeedback value)
    {
        if(value.EventId==0 || !value.TargetId.IsValid || !Enum.IsDefined(value.Control) || !Enum.IsDefined(value.Change) ||
            !double.IsFinite(value.Duration) || !double.IsFinite(value.Strength) ||
            (value.Mitigation&~(ControlMitigation.Immunity|ControlMitigation.Resistance|ControlMitigation.AdaptiveResistance))!=0)return false;
        if(value.Change is ControlChangeKind.Applied or ControlChangeKind.Refreshed)
            return value.Duration>0 && value.Strength>0 && value.RemainingInstances>0 && (value.Mitigation&ControlMitigation.Immunity)==0;
        if(value.Duration!=0 || value.Strength!=0)return false;
        return value.Change==ControlChangeKind.Rejected
            ? value.Mitigation is ControlMitigation.Immunity or ControlMitigation.Resistance or ControlMitigation.AdaptiveResistance
            : value.Mitigation==ControlMitigation.None;
    }
    public static NetDataWriter Write(ControlFeedback value)
    {
        if(!ValidControlFeedback(value))throw new ArgumentException("Invalid control feedback.");
        var writer=CreateWriter(NetworkMessageType.ControlFeedback);
        writer.Put(value.EventId);writer.Put(value.SourceId.Value);writer.Put(value.TargetId.Value);writer.Put(value.ServerTick);
        writer.Put((byte)value.Control);writer.Put((byte)value.Change);writer.Put(value.Duration);writer.Put(value.Strength);
        writer.Put(value.RemainingInstances);writer.Put((byte)value.Mitigation);return writer;
    }
    public static bool TryReadControlFeedback(NetDataReader reader,out ControlFeedback value)
    {
        value=default;
        if(reader.AvailableBytes!=51 || !reader.TryGetULong(out var id) || !reader.TryGetULong(out var source) ||
            !reader.TryGetULong(out var target) || !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var control) ||
            !reader.TryGetByte(out var change) || !reader.TryGetDouble(out var duration) || !reader.TryGetDouble(out var strength) ||
            !reader.TryGetUInt(out var remaining) || !reader.TryGetByte(out var mitigation))return false;
        var candidate=new ControlFeedback(id,new(source),new(target),tick,(CrowdControlKind)control,(ControlChangeKind)change,duration,strength,remaining,(ControlMitigation)mitigation);
        if(!ValidControlFeedback(candidate))return false;value=candidate;return true;
    }
}
