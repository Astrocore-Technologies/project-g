using LiteNetLib.Utils;
namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    private static bool ValidLife(ActorLifeState value)=>value.EntityId.IsValid && value.DeathId<=long.MaxValue &&
        double.IsFinite(value.CorpseRemaining)&&value.CorpseRemaining is >=0 and <=86400 &&
        double.IsFinite(value.RespawnRemaining)&&value.RespawnRemaining is >=0 and <=86400 &&
        (value.Dead?value.DeathId>0:value.CorpseRemaining==0&&value.RespawnRemaining==0);
    public static NetDataWriter Write(ActorLifeState value)
    {
        if(!ValidLife(value))throw new ArgumentException("Invalid actor life state.");
        var writer=CreateWriter(NetworkMessageType.ActorLifeState);writer.Put(value.EntityId.Value);writer.Put(value.ServerTick);writer.Put(value.DeathId);
        writer.Put(value.Dead);writer.Put(value.CorpseRemaining);writer.Put(value.RespawnRemaining);return writer;
    }
    public static bool TryReadActorLifeState(NetDataReader reader,out ActorLifeState value)
    {
        value=default;
        if(reader.AvailableBytes!=37 || !reader.TryGetULong(out var id) || !reader.TryGetUInt(out var tick) || !reader.TryGetULong(out var death) ||
            !reader.TryGetByte(out var dead) || dead>1 || !reader.TryGetDouble(out var corpse) || !reader.TryGetDouble(out var respawn))return false;
        var state=new ActorLifeState(new(id),tick,death,dead==1,corpse,respawn);if(!ValidLife(state))return false;value=state;return true;
    }
    public static NetDataWriter Write(RespawnCommand value)
    {
        if(value.Sequence==0 || value.DeathId==0 || value.DeathId>long.MaxValue)throw new ArgumentException("Invalid respawn command.");
        var writer=CreateWriter(NetworkMessageType.RespawnCommand);writer.Put(value.Sequence);writer.Put(value.DeathId);return writer;
    }
    public static bool TryReadRespawnCommand(NetDataReader reader,out RespawnCommand value)
    {
        value=default;
        if(reader.AvailableBytes!=12 || !reader.TryGetUInt(out var sequence) || sequence==0 || !reader.TryGetULong(out var death) || death==0 || death>long.MaxValue)return false;
        value=new(sequence,death);return true;
    }
    public static NetDataWriter Write(RespawnResult value)
    {
        if(value.Sequence==0 || !Enum.IsDefined(value.Outcome))throw new ArgumentException("Invalid respawn result.");
        var writer=CreateWriter(NetworkMessageType.RespawnResult);writer.Put(value.Sequence);writer.Put(value.ServerTick);writer.Put((byte)value.Outcome);return writer;
    }
    public static bool TryReadRespawnResult(NetDataReader reader,out RespawnResult value)
    {
        value=default;
        if(reader.AvailableBytes!=9 || !reader.TryGetUInt(out var sequence) || sequence==0 || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetByte(out var outcome) || !Enum.IsDefined((RespawnOutcome)outcome))return false;
        value=new(sequence,tick,(RespawnOutcome)outcome);return true;
    }
}
