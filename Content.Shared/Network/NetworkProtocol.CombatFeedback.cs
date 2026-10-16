using LiteNetLib.Utils;
namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    private static bool ValidFeedback(CombatFeedback value)=>value.EventId!=0 && value.TargetId.IsValid && Enum.IsDefined(value.Kind) &&
        double.IsFinite(value.Amount) && value.Amount>0 && (!value.Critical || value.Kind is CombatFeedbackKind.Healing or CombatFeedbackKind.PeriodicDamage);
    public static NetDataWriter Write(CombatFeedback value)
    {
        if(!ValidFeedback(value))throw new ArgumentException("Invalid combat feedback.");
        var writer=CreateWriter(NetworkMessageType.CombatFeedback);writer.Put(value.EventId);writer.Put(value.ExecutionId);
        writer.Put(value.SourceId.Value);writer.Put(value.TargetId.Value);writer.Put(value.ServerTick);writer.Put((byte)value.Kind);
        writer.Put(value.Amount);writer.Put(value.Critical);return writer;
    }
    public static bool TryReadCombatFeedback(NetDataReader reader,out CombatFeedback value)
    {
        value=default;
        if(reader.AvailableBytes!=46 || !reader.TryGetULong(out var id) || !reader.TryGetULong(out var execution) ||
            !reader.TryGetULong(out var source) || !reader.TryGetULong(out var target) || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetByte(out var kind) || !reader.TryGetDouble(out var amount) || !reader.TryGetByte(out var critical) || critical>1)return false;
        var candidate=new CombatFeedback(id,execution,new(source),new(target),tick,(CombatFeedbackKind)kind,amount,critical==1);
        if(!ValidFeedback(candidate))return false;value=candidate;return true;
    }
}
