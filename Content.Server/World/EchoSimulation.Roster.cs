using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class EchoSimulation
{
    private readonly Dictionary<NetworkEntityId,(uint Sequence,ulong Handle)> _summonRequests=new();
    private readonly Dictionary<NetworkEntityId,EchoSummonResult> _summonResults=new();
    private readonly HashSet<NetworkEntityId> _rosterDirty=new();
    private readonly List<NetworkEntityId> _finishedSummons=new();
    private readonly Dictionary<NetworkEntityId,EchoRecallResult> _recallResults=new();
    public IReadOnlyDictionary<NetworkEntityId,EchoRecallResult> RecallResults=>_recallResults;
    public IReadOnlyDictionary<NetworkEntityId,EchoSummonResult> SummonResults=>_summonResults;
    public bool IsRosterDirty(NetworkEntityId owner)=>_rosterDirty.Contains(owner)||_loadoutDirty.Contains(owner);

    public bool QueueRoster(NetworkEntityId owner,EchoRosterRequest command,uint tick)
    {
        if(!_owners.TryGetValue(owner,out var state)||state.Exiting||command.Sequence==0||
            !MovementSimulation.IsSequenceNewer(command.Sequence,state.RosterSequence)||state.RosterRequestTick==tick||
            (ulong)command.Page*NetworkConstants.EchoRosterPageSize>=(ulong)Math.Max(1,state.Roster.Count))return false;
        state.RosterSequence=command.Sequence;state.RosterRequestTick=tick;state.RosterPage=command.Page;_rosterDirty.Add(owner);return true;
    }
    public EchoRosterPage Roster(NetworkEntityId owner,uint tick)
    {
        var state=_owners[owner];var offset=checked((int)(state.RosterPage*NetworkConstants.EchoRosterPageSize));
        var count=Math.Min(NetworkConstants.EchoRosterPageSize,state.Roster.Count-offset);var entries=new EchoRosterEntry[count];
        var summoning=_combat.SummonState(owner);
        for(var i=0;i<count;i++)
        {
            var entry=state.Roster[offset+i];var saved=entry.Saved;
            var body=entry.Body;var live=body is not null&&!state.Exiting&&_combat.Get(body.Id).Health>0;
            var status=live?EchoAvailability.Active:saved.Availability==SavedEchoAvailability.RequiresRestoration||body is not null&&_combat.Get(body.Id).Health<=0?
                EchoAvailability.RequiresRestoration:saved.InstanceId==summoning.Instance?EchoAvailability.Summoning:EchoAvailability.Ready;
            var actor=body is not null&&!state.Exiting?_combat.Get(body.Id):null;
            entries[i]=new(entry.Handle,_catalog.Echoes[saved.DefinitionId].Name,status,live?body!.Slot:(byte)0,live?body!.Id:default,
                actor?.Health??saved.Health,actor?.Resources.Mana??saved.Mana,actor?.Resources.Stamina??saved.Defense.Stamina,
                status==EchoAvailability.Summoning?summoning.Sequence:0,status==EchoAvailability.Summoning?summoning.Execution:0,
                status==EchoAvailability.RequiresRestoration?actor?.DeathId??saved.DeathId:0,
                (Allows(entry,0)?EchoRefusal.None:EchoRefusal.Basic)|
                (Allows(entry,_catalog.Abilities[_catalog.Echoes[saved.DefinitionId].SignatureId].NetworkId)?EchoRefusal.None:EchoRefusal.Signature));
        }
        return new(owner,tick,state.RosterSequence,state.RosterPage,(uint)state.Roster.Count,entries,(byte)_catalog.EchoRecovery.Quantity);
    }
    public bool QueueSummon(NetworkEntityId owner,EchoSummonCommand command,uint tick)
    {
        if(!_owners.TryGetValue(owner,out var state)||state.Exiting||command.Sequence==0||command.Handle==0||
            !MovementSimulation.IsSequenceNewer(command.Sequence,state.SummonSequence)||state.SummonRequestTick==tick)return false;
        state.SummonSequence=command.Sequence;state.SummonRequestTick=tick;
        if(!state.Handles.TryGetValue(command.Handle,out var entry))
        {_summonResults[owner]=new(command.Sequence,tick,command.Handle,EchoSummonOutcome.InvalidEcho,0);return false;}
        if(!QueueSummon(owner,entry.Saved.InstanceId,command.Sequence,tick,command.InputOrder))return false;
        _summonRequests[owner]=(command.Sequence,command.Handle);return true;
    }
    private void CollectSummonResults(uint tick)
    {
        _finishedSummons.Clear();
        foreach(var (owner,request) in _summonRequests)
            if(_combat.SummonResults.TryGetValue(owner,out var result)&&result.Sequence==request.Sequence)
            {
                _summonResults[owner]=new(result.Sequence,tick,request.Handle,MapSummonOutcome(result.Outcome),result.Execution);
                _rosterDirty.Add(owner);
                if(result.Outcome is not (CompanionSummonOutcome.Accepted or CompanionSummonOutcome.Buffered))_finishedSummons.Add(owner);
            }
        foreach(var id in _finishedSummons)_summonRequests.Remove(id);
    }
    public bool QueueSummonControl(NetworkEntityId owner,EchoSummonControlCommand command,uint tick)
    {
        if(!_owners.TryGetValue(owner,out var state)||state.Exiting||command.Sequence==0||command.ActionSequence==0||
            !MovementSimulation.IsSequenceNewer(command.Sequence,state.SummonControlSequence)||state.SummonControlTick==tick)return false;
        state.SummonControlSequence=command.Sequence;state.SummonControlTick=tick;
        var cancelled=_combat.CancelSummon(owner,command.ActionSequence,command.Execution,tick);
        if(cancelled){_rosterDirty.Add(owner);_dirty.Add(owner);}return cancelled;
    }
    public bool QueueRecall(NetworkEntityId owner,EchoRecallCommand command,uint tick)
    {
        if(!_owners.TryGetValue(owner,out var state)||state.Exiting||command.Sequence==0||command.Handle==0||
            !MovementSimulation.IsSequenceNewer(command.Sequence,state.RecallSequence)||state.RecallRequestTick==tick)return false;
        state.RecallSequence=command.Sequence;state.RecallRequestTick=tick;
        var master=_combat.Get(owner);state.Handles.TryGetValue(command.Handle,out var entry);
        var body=entry?.Body;
        var outcome=entry is null?EchoRecallOutcome.InvalidEcho:master.Health<=0||!master.Active||body is null||_combat.Get(body.Id).Health<=0?
            EchoRecallOutcome.InvalidState:_combat.InCombat(owner)?EchoRecallOutcome.InCombat:EchoRecallOutcome.Completed;
        if(outcome==EchoRecallOutcome.Completed)
        {
            _abilities.FinishDeparture(body!.Id);SaveBody(entry!,body,false);entry!.Body=null;state.Slots[body.Slot-1]=null;RemoveBody(body);
            _dirty.Add(owner);_loadoutDirty.Add(owner);
        }
        _recallResults[owner]=new(command.Sequence,tick,command.Handle,outcome);return true;
    }
    private static EchoSummonOutcome MapSummonOutcome(CompanionSummonOutcome value)=>value switch
    {
        CompanionSummonOutcome.Accepted=>EchoSummonOutcome.Accepted,CompanionSummonOutcome.Buffered=>EchoSummonOutcome.Buffered,
        CompanionSummonOutcome.Completed=>EchoSummonOutcome.Completed,CompanionSummonOutcome.Cancelled=>EchoSummonOutcome.Cancelled,
        CompanionSummonOutcome.Interrupted=>EchoSummonOutcome.Interrupted,CompanionSummonOutcome.InvalidState=>EchoSummonOutcome.InvalidState,
        CompanionSummonOutcome.InvalidEcho=>EchoSummonOutcome.InvalidEcho,CompanionSummonOutcome.ActiveLimit=>EchoSummonOutcome.ActiveLimit,
        CompanionSummonOutcome.AlreadyActive=>EchoSummonOutcome.AlreadyActive,CompanionSummonOutcome.NoMana=>EchoSummonOutcome.NoMana,
        _=>EchoSummonOutcome.Unavailable
    };
}
