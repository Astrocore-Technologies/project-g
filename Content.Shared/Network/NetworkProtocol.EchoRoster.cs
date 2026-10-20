using System.Text;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(EchoRestoreCommand value)
    {
        if(value.Sequence==0||value.Handle==0||value.DeathId is 0 or >long.MaxValue)throw new ArgumentException("Invalid Echo restore command.");
        var writer=CreateWriter(NetworkMessageType.EchoRestoreCommand);writer.Put(value.Sequence);writer.Put(value.Handle);writer.Put(value.DeathId);return writer;
    }
    public static bool TryReadEchoRestoreCommand(NetDataReader reader,out EchoRestoreCommand value)
    {
        value=default;if(reader.AvailableBytes!=20||!reader.TryGetUInt(out var sequence)||sequence==0||!reader.TryGetULong(out var handle)||handle==0||
            !reader.TryGetULong(out var death)||death is 0 or >long.MaxValue)return false;
        value=new(sequence,handle,death);return true;
    }
    public static NetDataWriter Write(EchoRestoreResult value)
    {
        if(value.Sequence==0||value.Handle==0||!Enum.IsDefined(value.Outcome))throw new ArgumentException("Invalid Echo restore result.");
        var writer=CreateWriter(NetworkMessageType.EchoRestoreResult);writer.Put(value.Sequence);writer.Put(value.ServerTick);writer.Put(value.Handle);writer.Put((byte)value.Outcome);return writer;
    }
    public static bool TryReadEchoRestoreResult(NetDataReader reader,out EchoRestoreResult value)
    {
        value=default;if(reader.AvailableBytes!=17||!reader.TryGetUInt(out var sequence)||sequence==0||!reader.TryGetUInt(out var tick)||
            !reader.TryGetULong(out var handle)||handle==0||!reader.TryGetByte(out var outcome)||!Enum.IsDefined((EchoRestoreOutcome)outcome))return false;
        value=new(sequence,tick,handle,(EchoRestoreOutcome)outcome);return true;
    }
    public static NetDataWriter Write(EchoSummonControlCommand value)
    {
        if(value.Sequence==0||value.ActionSequence==0)throw new ArgumentException("Invalid Echo summon control.");
        var writer=CreateWriter(NetworkMessageType.EchoSummonControlCommand);writer.Put(value.Sequence);writer.Put(value.ActionSequence);writer.Put(value.Execution);return writer;
    }
    public static bool TryReadEchoSummonControlCommand(NetDataReader reader,out EchoSummonControlCommand value)
    {
        value=default;if(reader.AvailableBytes!=16||!reader.TryGetUInt(out var sequence)||sequence==0||!reader.TryGetUInt(out var action)||action==0||!reader.TryGetULong(out var execution))return false;
        value=new(sequence,action,execution);return true;
    }
    public static NetDataWriter Write(EchoRecallCommand value)
    {
        if(value.Sequence==0||value.Handle==0)throw new ArgumentException("Invalid Echo recall command.");
        var writer=CreateWriter(NetworkMessageType.EchoRecallCommand);writer.Put(value.Sequence);writer.Put(value.Handle);return writer;
    }
    public static bool TryReadEchoRecallCommand(NetDataReader reader,out EchoRecallCommand value)
    {
        value=default;if(reader.AvailableBytes!=12||!reader.TryGetUInt(out var sequence)||sequence==0||!reader.TryGetULong(out var handle)||handle==0)return false;
        value=new(sequence,handle);return true;
    }
    public static NetDataWriter Write(EchoRecallResult value)
    {
        if(value.Sequence==0||value.Handle==0||!Enum.IsDefined(value.Outcome))throw new ArgumentException("Invalid Echo recall result.");
        var writer=CreateWriter(NetworkMessageType.EchoRecallResult);writer.Put(value.Sequence);writer.Put(value.ServerTick);
        writer.Put(value.Handle);writer.Put((byte)value.Outcome);return writer;
    }
    public static bool TryReadEchoRecallResult(NetDataReader reader,out EchoRecallResult value)
    {
        value=default;if(reader.AvailableBytes!=17||!reader.TryGetUInt(out var sequence)||sequence==0||!reader.TryGetUInt(out var tick)||
            !reader.TryGetULong(out var handle)||handle==0||!reader.TryGetByte(out var outcome)||!Enum.IsDefined((EchoRecallOutcome)outcome))return false;
        value=new(sequence,tick,handle,(EchoRecallOutcome)outcome);return true;
    }
    private static bool ValidRosterEntry(EchoRosterEntry value)=>value.Handle!=0 && value.Name is {Length:>0 and <=24} &&
        Encoding.UTF8.GetByteCount(value.Name)<=48 && Enum.IsDefined(value.Availability) &&
        (value.Availability==EchoAvailability.Active ? EchoSlotValid(value.Slot)&&value.EntityId.IsValid : value.Slot==0&&!value.EntityId.IsValid) &&
        (value.Availability==EchoAvailability.Summoning ? value.ActionSequence!=0&&value.Execution!=0 : value.ActionSequence==0&&value.Execution==0) &&
        (value.Availability==EchoAvailability.RequiresRestoration ? value.DeathId is >0 and <=long.MaxValue : value.DeathId==0) &&
        (value.Refusal&~(EchoRefusal.Basic|EchoRefusal.Signature))==0 &&
        double.IsFinite(value.Health)&&value.Health>=0 && (value.Availability==EchoAvailability.RequiresRestoration)==(value.Health==0) &&
        double.IsFinite(value.Mana)&&value.Mana>=0 && double.IsFinite(value.Stamina)&&value.Stamina>=0 &&
        value.Experience>=0 && double.IsFinite(value.ExperienceFraction) && value.ExperienceFraction is >=0 and <1;

    public static NetDataWriter Write(EchoRosterRequest value)
    {
        if(value.Sequence==0)throw new ArgumentException("Invalid Echo roster request.");
        var writer=CreateWriter(NetworkMessageType.EchoRosterRequest);writer.Put(value.Sequence);writer.Put(value.Page);return writer;
    }
    public static bool TryReadEchoRosterRequest(NetDataReader reader,out EchoRosterRequest value)
    {
        value=default;if(reader.AvailableBytes!=8 || !reader.TryGetUInt(out var sequence)||sequence==0||!reader.TryGetUInt(out var page))return false;
        value=new(sequence,page);return true;
    }
    public static NetDataWriter Write(EchoSummonCommand value)
    {
        if(value.Sequence==0||value.Handle==0||value.InputOrder==0)throw new ArgumentException("Invalid Echo summon command.");
        var writer=CreateWriter(NetworkMessageType.EchoSummonCommand);writer.Put(value.Sequence);writer.Put(value.Handle);writer.Put(value.InputOrder);return writer;
    }
    public static bool TryReadEchoSummonCommand(NetDataReader reader,out EchoSummonCommand value)
    {
        value=default;if(reader.AvailableBytes!=16||!reader.TryGetUInt(out var sequence)||sequence==0||!reader.TryGetULong(out var handle)||handle==0||!reader.TryGetUInt(out var inputOrder)||inputOrder==0)return false;
        value=new(sequence,handle,inputOrder);return true;
    }
    public static NetDataWriter Write(EchoSummonResult value)
    {
        if(value.Sequence==0||value.Handle==0||!Enum.IsDefined(value.Outcome))throw new ArgumentException("Invalid Echo summon result.");
        var writer=CreateWriter(NetworkMessageType.EchoSummonResult);writer.Put(value.Sequence);writer.Put(value.ServerTick);
        writer.Put(value.Handle);writer.Put((byte)value.Outcome);writer.Put(value.Execution);return writer;
    }
    public static bool TryReadEchoSummonResult(NetDataReader reader,out EchoSummonResult value)
    {
        value=default;if(reader.AvailableBytes!=25||!reader.TryGetUInt(out var sequence)||sequence==0||!reader.TryGetUInt(out var tick)||
            !reader.TryGetULong(out var handle)||handle==0||!reader.TryGetByte(out var outcome)||!Enum.IsDefined((EchoSummonOutcome)outcome)||!reader.TryGetULong(out var execution))return false;
        value=new(sequence,tick,handle,(EchoSummonOutcome)outcome,execution);return true;
    }
    private static bool ValidRosterPage(uint page,uint total,int count)
    {
        var offset=(ulong)page*NetworkConstants.EchoRosterPageSize;
        return count<=NetworkConstants.EchoRosterPageSize && offset<=total &&
            count==(int)Math.Min((ulong)NetworkConstants.EchoRosterPageSize,total-offset) && (count!=0 || page==0&&total==0);
    }
    public static NetDataWriter Write(EchoRosterPage value)
    {
        if(!value.OwnerId.IsValid||value.Entries is null||value.RestorationCost is <1 or >NetworkConstants.MaxInventoryItems||!ValidRosterPage(value.Page,value.Total,value.Entries.Count))throw new ArgumentException("Invalid Echo roster page.");
        var writer=CreateWriter(NetworkMessageType.EchoRosterPage);writer.Put(value.OwnerId.Value);writer.Put(value.ServerTick);
        writer.Put(value.Sequence);writer.Put(value.Page);writer.Put(value.Total);writer.Put(value.RestorationCost);writer.Put((byte)value.Entries.Count);
        var handles=new HashSet<ulong>();var slots=new HashSet<byte>();var bodies=new HashSet<NetworkEntityId>();
        foreach(var entry in value.Entries)
        {
            if(!ValidRosterEntry(entry)||!handles.Add(entry.Handle)||entry.EntityId==value.OwnerId||
                entry.Availability==EchoAvailability.Active&&(!slots.Add(entry.Slot)||!bodies.Add(entry.EntityId)))throw new ArgumentException("Invalid Echo roster entry.");
            writer.Put(entry.Handle);writer.Put(entry.Name);writer.Put((byte)entry.Availability);writer.Put(entry.Slot);writer.Put(entry.EntityId.Value);
            writer.Put(entry.Health);writer.Put(entry.Mana);writer.Put(entry.Stamina);writer.Put(entry.ActionSequence);writer.Put(entry.Execution);writer.Put(entry.DeathId);writer.Put((byte)entry.Refusal);
            writer.Put(entry.Experience);writer.Put(entry.ExperienceFraction);
        }
        return writer;
    }
    public static bool TryReadEchoRosterPage(NetDataReader reader,out EchoRosterPage value)
    {
        value=default;
        if(!reader.TryGetULong(out var owner)||owner==0||!reader.TryGetUInt(out var tick)||!reader.TryGetUInt(out var sequence)||
            !reader.TryGetUInt(out var page)||!reader.TryGetUInt(out var total)||!reader.TryGetByte(out var cost)||cost is <1 or >NetworkConstants.MaxInventoryItems||
            !reader.TryGetByte(out var count)||!ValidRosterPage(page,total,count))return false;
        var entries=new EchoRosterEntry[count];var handles=new HashSet<ulong>();var slots=new HashSet<byte>();var bodies=new HashSet<NetworkEntityId>();
        for(var i=0;i<count;i++)
        {
            if(!reader.TryGetULong(out var handle)||!reader.TryGetString(out var name)||!reader.TryGetByte(out var state)||
                !reader.TryGetByte(out var slot)||!reader.TryGetULong(out var body)||!reader.TryGetDouble(out var hp)||
                !reader.TryGetDouble(out var mp)||!reader.TryGetDouble(out var stamina)||!reader.TryGetUInt(out var action)||!reader.TryGetULong(out var execution)||!reader.TryGetULong(out var death)||!reader.TryGetByte(out var refusal)||
                !reader.TryGetLong(out var experience)||!reader.TryGetDouble(out var fraction))return false;
            var entry=new EchoRosterEntry(handle,name,(EchoAvailability)state,slot,new(body),hp,mp,stamina,action,execution,death,(EchoRefusal)refusal,experience,fraction);
            if(!ValidRosterEntry(entry)||!handles.Add(handle)||body==owner||entry.Availability==EchoAvailability.Active&&(!slots.Add(slot)||!bodies.Add(entry.EntityId)))return false;
            entries[i]=entry;
        }
        if(reader.AvailableBytes!=0)return false;
        value=new(new(owner),tick,sequence,page,total,entries,cost);return true;
    }
}
