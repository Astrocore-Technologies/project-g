using Content.Shared.Combat;
using LiteNetLib.Utils;
namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(CombatJournalRequest value)
    {
        if(value.Sequence==0 || value.DeathId>long.MaxValue)throw new ArgumentException("Invalid journal request.");
        var w=CreateWriter(NetworkMessageType.CombatJournalRequest);w.Put(value.Sequence);w.Put(value.DeathId);return w;
    }
    public static bool TryReadCombatJournalRequest(NetDataReader r,out CombatJournalRequest value)
    {
        value=default;if(r.AvailableBytes!=12 || !r.TryGetUInt(out var sequence) || sequence==0 || !r.TryGetULong(out var death) || death>long.MaxValue)return false;
        value=new(sequence,death);return true;
    }
    private static bool ValidJournalIndex(CombatJournalIndex value)
    {
        if(!value.OwnerId.IsValid || value.Deaths is null || value.Deaths.Length>10)return false;ulong previous=0;
        foreach(var death in value.Deaths)
        {if(death.DeathId<=previous || death.DeathId>long.MaxValue || death.AtUnixMilliseconds is <0 or >253402300799999 || death.EventCount is 0 or >300)return false;previous=death.DeathId;}
        return true;
    }
    public static NetDataWriter Write(CombatJournalIndex value)
    {
        if(!ValidJournalIndex(value))throw new ArgumentException("Invalid journal index.");
        var w=CreateWriter(NetworkMessageType.CombatJournalIndex);w.Put(value.OwnerId.Value);w.Put(value.ServerTick);w.Put((byte)value.Deaths.Length);
        foreach(var death in value.Deaths){w.Put(death.DeathId);w.Put(death.AtUnixMilliseconds);w.Put(death.EventCount);}return w;
    }
    public static bool TryReadCombatJournalIndex(NetDataReader r,out CombatJournalIndex value)
    {
        value=default;if(!r.TryGetULong(out var owner) || !r.TryGetUInt(out var tick) || !r.TryGetByte(out var count) || count>10 || r.AvailableBytes!=count*18)return false;
        var deaths=new DeathLogSummary[count];
        for(var i=0;i<count;i++)
        {if(!r.TryGetULong(out var id) || !r.TryGetLong(out var at) || !r.TryGetUShort(out var total))return false;deaths[i]=new(id,at,total);}
        var candidate=new CombatJournalIndex(new(owner),tick,deaths);if(!ValidJournalIndex(candidate))return false;value=candidate;return true;
    }
    private static bool ValidJournalChunk(CombatJournalChunk value)
    {
        if(!value.OwnerId.IsValid || value.DeathId>long.MaxValue || value.Entries is null || value.Entries.Length>2)return false;
        if(value.Sequence==0)
        {if(value.DeathId!=0 || value.Start!=0 || value.Total!=0 || value.Entries.Length==0)return false;}
        else if(value.Total>(value.DeathId==0?200:300) || value.Start>value.Total || value.Entries.Length>value.Total-value.Start ||
            value.Total>0&&value.Entries.Length==0 || value.DeathId>0&&value.Total==0)return false;
        ulong previous=0;
        foreach(var entry in value.Entries){if(!entry.IsValid || entry.Id<=previous)return false;previous=entry.Id;}return true;
    }
    public static NetDataWriter Write(CombatJournalChunk value)
    {
        if(!ValidJournalChunk(value))throw new ArgumentException("Invalid journal chunk.");
        var w=CreateWriter(NetworkMessageType.CombatJournalChunk);w.Put(value.OwnerId.Value);w.Put(value.Sequence);w.Put(value.DeathId);
        w.Put(value.Start);w.Put(value.Total);w.Put((byte)value.Entries.Length);
        foreach(var e in value.Entries)
        {w.Put(e.Id);w.Put(e.AtUnixMilliseconds);w.Put((byte)e.Kind);w.Put(e.Source);w.Put(e.Target);w.Put(e.Amount);w.Put(e.Duration);w.Put(e.Strength);w.Put(e.SkillId);w.Put((byte)e.Control);w.Put((byte)e.Flags);w.Put((byte)e.HitTags);}
        if(w.Length>NetworkConstants.MaxGamePacketBytes-RegionEnvelopeBytes)throw new ArgumentException("Journal chunk exceeds packet budget.");return w;
    }
    public static bool TryReadCombatJournalChunk(NetDataReader r,out CombatJournalChunk value)
    {
        value=default;if(r.AvailableBytes>NetworkConstants.MaxGamePacketBytes-RegionEnvelopeBytes-2 || !r.TryGetULong(out var owner) ||
            !r.TryGetUInt(out var sequence) || !r.TryGetULong(out var death) || !r.TryGetUShort(out var start) || !r.TryGetUShort(out var total) || !r.TryGetByte(out var count) || count>2)return false;
        var entries=new CombatJournalEntry[count];
        for(var i=0;i<count;i++)
        {
            if(!r.TryGetULong(out var id) || !r.TryGetLong(out var at) || !r.TryGetByte(out var kind) || !r.TryGetString(out var source) ||
                !r.TryGetString(out var target) || !r.TryGetDouble(out var amount) || !r.TryGetDouble(out var duration) || !r.TryGetDouble(out var strength) ||
                !r.TryGetUShort(out var skill) || !r.TryGetByte(out var control) || !r.TryGetByte(out var flags) || !r.TryGetByte(out var tags))return false;
            entries[i]=new(id,at,(CombatJournalKind)kind,source,target,amount,duration,strength,skill,(CrowdControlKind)control,(CombatJournalFlags)flags,(CombatHitTags)tags);
        }
        var candidate=new CombatJournalChunk(new(owner),sequence,death,start,total,entries);if(r.AvailableBytes!=0 || !ValidJournalChunk(candidate))return false;value=candidate;return true;
    }
    public static NetDataWriter Write(CombatJournalResult value)
    {
        if(value.Sequence==0 || !Enum.IsDefined(value.Outcome))throw new ArgumentException("Invalid journal result.");
        var w=CreateWriter(NetworkMessageType.CombatJournalResult);w.Put(value.Sequence);w.Put(value.ServerTick);w.Put((byte)value.Outcome);return w;
    }
    public static bool TryReadCombatJournalResult(NetDataReader r,out CombatJournalResult value)
    {
        value=default;if(r.AvailableBytes!=9 || !r.TryGetUInt(out var sequence) || sequence==0 || !r.TryGetUInt(out var tick) || !r.TryGetByte(out var outcome) || !Enum.IsDefined((CombatJournalOutcome)outcome))return false;
        value=new(sequence,tick,(CombatJournalOutcome)outcome);return true;
    }
}
