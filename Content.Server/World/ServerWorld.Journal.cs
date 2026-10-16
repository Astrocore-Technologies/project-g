using Content.Shared.Combat;
using Content.Shared.Movement;
using Content.Shared.Network;
namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private readonly Dictionary<int,CombatJournalRequest> _journalPending=new();
    private readonly Dictionary<int,(uint Sequence,uint Tick)> _journalRequests=new();
    internal sealed record JournalReply(CombatJournalRequest Request,CombatJournalEntry[]? Entries,CombatJournalOutcome Outcome=default);
    private readonly Dictionary<NetworkEntityId,JournalReply> _journalReplies=new();
    internal IReadOnlyDictionary<NetworkEntityId,JournalReply> JournalReplies=>_journalReplies;
    internal bool TryQueueJournalRequest(int connection,CombatJournalRequest request)
    {
        if(request.Sequence==0 || request.DeathId>long.MaxValue || !_playersByConnection.TryGetValue(connection,out var player) || !player.Loaded)return false;
        if(_journalRequests.TryGetValue(connection,out var previous))
        {
            if(!MovementSimulation.IsSequenceNewer(request.Sequence,previous.Sequence))return false;
            if(previous.Tick==Tick)
            {_journalRequests[connection]=(request.Sequence,Tick);_journalReplies[player.EntityId]=new(request,null,CombatJournalOutcome.RateLimited);return false;}
        }
        _journalRequests[connection]=(request.Sequence,Tick);_journalPending[connection]=request;return true;
    }
    private void SimulateJournalRequests()
    {
        foreach(var (connection,request) in _journalPending)
        {
            var owner=_playersByConnection[connection].EntityId;CombatJournalEntry[]? entries=null;
            if(_journalReplies.TryGetValue(owner,out var rejected)&&rejected.Request.Sequence!=request.Sequence)continue;
            if(request.DeathId==0)entries=Combat!.CurrentJournal(owner);
            else foreach(var death in Combat!.DeathLogs(owner))if(death.DeathId==request.DeathId){entries=death.Entries;break;}
            _journalReplies[owner]=new(request,entries,CombatJournalOutcome.NotFound);
        }
        _journalPending.Clear();
    }
    private void ResetJournalSession(int connection)
    {
        _journalPending.Remove(connection);_journalRequests.Remove(connection);
        if(_playersByConnection.TryGetValue(connection,out var player))_journalReplies.Remove(player.EntityId);
    }
}
