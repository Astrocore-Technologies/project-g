using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib;
namespace Content.Server.Networking;

public sealed partial class GameServerService
{
    private void SendCombatJournal(NetPeer peer,InterestView view,ServerWorld world)
    {
        if(world.Combat is not {} combat || !world.TryGetOwnedEntity(peer.Id,out var owner))return;
        var deaths=combat.DeathLogs(owner);var lastDeath=deaths.Count==0?0:deaths[^1].DeathId;
        if(!view.JournalInitialized || view.JournalDeathId!=lastDeath)
        {
            var index=new DeathLogSummary[deaths.Count];
            for(var i=0;i<index.Length;i++)index[i]=new(deaths[i].DeathId,deaths[i].AtUnixMilliseconds,(ushort)deaths[i].Entries.Length);
            SendGame(peer,NetworkProtocol.Write(new CombatJournalIndex(owner,world.Tick,index)),DeliveryMethod.ReliableOrdered);view.JournalDeathId=lastDeath;
        }
        var serial=combat.JournalLastId(owner);
        if(serial!=view.JournalSerial)
        {
            var entries=combat.CurrentJournal(owner);
            foreach(var entry in entries)if(entry.Id>view.JournalSerial)
                SendGame(peer,NetworkProtocol.Write(new CombatJournalChunk(owner,0,0,0,0,[entry])),DeliveryMethod.ReliableOrdered);
            view.JournalSerial=serial;
        }
        view.JournalInitialized=true;
        if(!world.JournalReplies.TryGetValue(owner,out var reply))return;
        if(reply.Entries is not {} requested)
        {SendGame(peer,NetworkProtocol.Write(new CombatJournalResult(reply.Request.Sequence,world.Tick,reply.Outcome)),DeliveryMethod.ReliableOrdered);return;}
        if(requested.Length==0)
            SendGame(peer,NetworkProtocol.Write(new CombatJournalChunk(owner,reply.Request.Sequence,reply.Request.DeathId,0,0,[])),DeliveryMethod.ReliableOrdered);
        for(var i=0;i<requested.Length;i+=2)
        {
            var count=Math.Min(2,requested.Length-i);var chunk=new CombatJournalEntry[count];Array.Copy(requested,i,chunk,0,count);
            SendGame(peer,NetworkProtocol.Write(new CombatJournalChunk(owner,reply.Request.Sequence,reply.Request.DeathId,(ushort)i,(ushort)requested.Length,chunk)),DeliveryMethod.ReliableOrdered);
        }
    }
}
