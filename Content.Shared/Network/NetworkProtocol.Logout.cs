using LiteNetLib.Utils;
namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(LogoutCommand value)
    {
        if(value.Sequence==0) throw new ArgumentException("Invalid logout request.");
        var writer=CreateWriter(NetworkMessageType.LogoutCommand);writer.Put(value.Sequence);return writer;
    }
    public static bool TryReadLogoutCommand(NetDataReader reader,out LogoutCommand value)
    {
        value=default;
        if(reader.AvailableBytes!=4 || !reader.TryGetUInt(out var sequence) || sequence==0)return false;
        value=new(sequence);return true;
    }
    public static NetDataWriter Write(LogoutResult value)
    {
        if(value.Sequence==0 || !Enum.IsDefined(value.Outcome))throw new ArgumentException("Invalid logout result.");
        var writer=CreateWriter(NetworkMessageType.LogoutResult);writer.Put(value.Sequence);writer.Put(value.ServerTick);writer.Put((byte)value.Outcome);return writer;
    }
    public static bool TryReadLogoutResult(NetDataReader reader,out LogoutResult value)
    {
        value=default;
        if(reader.AvailableBytes!=9 || !reader.TryGetUInt(out var sequence) || sequence==0 || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetByte(out var outcome) || !Enum.IsDefined((LogoutOutcome)outcome))return false;
        value=new(sequence,tick,(LogoutOutcome)outcome);return true;
    }
}
