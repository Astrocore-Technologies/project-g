using Content.Server.Data;
using Content.Shared.Combat;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.Combat;

internal readonly record struct CompanionSummonCommand(uint Sequence, Guid Instance,uint InputOrder=0)
{
    public uint InputOrder {get;init;}=InputOrder==0?Sequence:InputOrder;
}
internal enum CompanionSummonOutcome : byte
{ Accepted, Buffered, Completed, Cancelled, Interrupted, InvalidState, InvalidEcho, ActiveLimit, AlreadyActive, NoMana, Unavailable }
internal readonly record struct CompanionSummonResult(uint Sequence,uint Tick,CompanionSummonOutcome Outcome,ulong Execution=0);

public sealed partial class CombatSimulation
{
    private sealed record SummonExecution(CompanionSummonCommand Command,ActionInstance Action);
    private readonly Dictionary<NetworkEntityId,SummonExecution> _summons=new();
    private readonly Dictionary<NetworkEntityId,(uint Sequence,uint Tick)> _summonIngress=new();
    private readonly Dictionary<NetworkEntityId,CompanionSummonResult> _summonResults=new();
    private readonly List<NetworkEntityId> _summonWork=new();
    // The coordinator checks owned records and active slots; transport never receives a persistent instance UUID.
    internal Func<NetworkEntityId,Guid,CompanionSummonOutcome>? SummonEligibility {get;set;}
    internal Action<NetworkEntityId,Guid>? SummonActivated {get;set;}
    internal IReadOnlyDictionary<NetworkEntityId,CompanionSummonResult> SummonResults=>_summonResults;
    internal (Guid Instance,uint Sequence,ulong Execution) SummonState(NetworkEntityId owner)=>_summons.TryGetValue(owner,out var summon)
        ?(summon.Command.Instance,summon.Command.Sequence,summon.Action.ExecutionId):default;

    internal bool QueueSummon(NetworkEntityId id,CompanionSummonCommand command,uint tick)
    {
        if(!_actors.TryGetValue(id,out var actor) || actor.Kind!=CombatEntityKind.Player || !actor.Active ||
            command.Sequence==0 || command.Instance==Guid.Empty ||
            _summonIngress.TryGetValue(id,out var previous) && !MovementSimulation.IsSequenceNewer(command.Sequence,previous.Sequence))return false;
        _summonIngress[id]=(command.Sequence,tick);
        if(previous.Sequence!=0 && previous.Tick==tick)return false;
        Inputs.Submit(id,new(OrdinaryInputKind.Companion,default,default,default,0,InputDeadline,0,
            Capabilities:_catalog.EchoSummon.Action.Capabilities,Invalidated:IsStunned(id) || actor.Control!=CombatControlPhase.None ||
                BlocksAction(id,_catalog.EchoSummon.Action.Capabilities),Companion:command));
        return true;
    }

    private void SetSummonResult(NetworkEntityId id,CompanionSummonCommand command,uint tick,CompanionSummonOutcome outcome,ulong execution=0)
        =>_summonResults[id]=new(command.Sequence,tick,outcome,execution);

    private InputDisposition ExecuteSummonInput(NetworkEntityId id,CompanionSummonCommand command,uint tick)
    {
        var actor=_actors[id];var profile=_catalog.EchoSummon;
        var outcome=actor.Health<=0 || !actor.Active || IsStunned(id) || actor.Control!=CombatControlPhase.None ||
                BlocksAction(id,profile.Action.Capabilities) || !_defense[id].Connected
            ? CompanionSummonOutcome.InvalidState
            : SummonActivated is null ? CompanionSummonOutcome.Unavailable
            : SummonEligibility?.Invoke(id,command.Instance) ?? CompanionSummonOutcome.Unavailable;
        if(outcome==CompanionSummonOutcome.Accepted && actor.Resources.Mana<profile.ManaCost)outcome=CompanionSummonOutcome.NoMana;
        if(outcome!=CompanionSummonOutcome.Accepted)
        {SetSummonResult(id,command,tick,outcome);return InputDisposition.Rejected;}
        var canCancel=actor.Action?.CanCancel(ActionCancelPermission.Skill)==true;
        if((actor.Action is {Finished:false} || IsActionLocked(id)) && !canCancel || IsDefending(id))
        {SetSummonResult(id,command,tick,CompanionSummonOutcome.Buffered);return InputDisposition.Waiting;}
        if(canCancel)
        {
            if(!CancelOrdinaryAction(id,ActionCancelPermission.Skill))
            {SetSummonResult(id,command,tick,CompanionSummonOutcome.Buffered);return InputDisposition.Waiting;}
            SetRecovery(id,0);return ExecuteSummonInput(id,command,tick);
        }
        if(!ExecutionCapacityAvailable)
        {SetSummonResult(id,command,tick,CompanionSummonOutcome.Unavailable);return InputDisposition.Rejected;}
        StopContinuousBasic(id);PlayerAction?.Invoke(id);
        OrdinaryActionAccepted?.Invoke(id,CombatActionCategory.Companion);
        NotifyNavigationAction(id,command.InputOrder);
        var action=new ActionInstance(AllocateExecution(),id,command.Sequence,"echo_summon",profile.Action,
            profile.Action.Durations(_calculator,actor.Stats),_time);
        actor.Action=action;_summons.Add(id,new(command,action));_defenseDirty.Add(id);
        SetSummonResult(id,command,tick,CompanionSummonOutcome.Accepted,action.ExecutionId);
        return InputDisposition.Executed;
    }

    private void AdvanceSummons(uint tick)
    {
        _summonWork.Clear();_summonWork.AddRange(_summons.Keys);
        foreach(var id in _summonWork)
        {
            if(!_summons.TryGetValue(id,out var summon) || !summon.Action.AwaitingActivation(_time))continue;
            var actor=_actors[id];var action=summon.Action;var profile=_catalog.EchoSummon;
            if(actor.Health<=0 || !actor.Active)
            {StopSummon(id,ActionStopReason.Death,tick:tick);continue;}
            var eligibility=SummonEligibility?.Invoke(id,summon.Command.Instance) ?? CompanionSummonOutcome.Unavailable;
            if(eligibility!=CompanionSummonOutcome.Accepted || SummonActivated is null)
            {
                StopSummon(id,ActionStopReason.Policy,tick:tick);
                SetSummonResult(id,summon.Command,tick,eligibility);continue;
            }
            if(actor.Resources.Mana<profile.ManaCost)
            {StopSummon(id,ActionStopReason.ResourceFailure,tick:tick);continue;}
            if(!action.Activate(_time))continue;
            DrainActorMana(id,profile.ManaCost);
            RecordJournal(id,id,CombatJournalKind.ActionActivated,profile.ManaCost);
            action.Advance(_time);FinishSummon(id,summon);
            SummonActivated(id,summon.Command.Instance);
            SetSummonResult(id,summon.Command,tick,CompanionSummonOutcome.Completed,action.ExecutionId);
        }
    }

    private void FinishSummon(NetworkEntityId id,SummonExecution summon)
    {
        if(!_summons.TryGetValue(id,out var current) || !ReferenceEquals(current,summon))return;
        _summons.Remove(id);
        if(_actors.TryGetValue(id,out var actor) && ReferenceEquals(actor.Action,summon.Action))actor.Action=null;
        _defenseDirty.Add(id);
    }

    internal bool StopSummon(NetworkEntityId id,ActionStopReason reason,ActionCancelPermission permission=ActionCancelPermission.Voluntary,
        uint sequence=0,ulong execution=0,ActionCapability? capabilities=null,uint? tick=null)
    {
        if(!_summons.TryGetValue(id,out var summon) || sequence!=0 && summon.Command.Sequence!=sequence ||
            execution!=0 && summon.Action.ExecutionId!=execution ||
            capabilities is {} required && (summon.Action.Definition.Capabilities&required)==0 || !summon.Action.Stop(reason,permission))return false;
        var paid=DrainActorMana(id,_catalog.EchoSummon.ManaCost*summon.Action.ResourceFractionForStop);
        RecordJournal(id,id,reason==ActionStopReason.Voluntary?CombatJournalKind.ActionCancelled:CombatJournalKind.ActionInterrupted,paid);
        FinishSummon(id,summon);
        SetSummonResult(id,summon.Command,tick??_simulationTick,reason==ActionStopReason.ResourceFailure ? CompanionSummonOutcome.NoMana :
            reason is ActionStopReason.Voluntary or ActionStopReason.Departure or ActionStopReason.Policy ? CompanionSummonOutcome.Cancelled : CompanionSummonOutcome.Interrupted,
            summon.Action.ExecutionId);
        return true;
    }

    internal bool CancelSummon(NetworkEntityId id,uint sequence,ulong execution,uint tick)
    {
        if(sequence==0)return false;
        var cancelled=execution==0 && Inputs.CancelCompanion(id,sequence);
        if(cancelled)_summonResults[id]=new(sequence,tick,CompanionSummonOutcome.Cancelled);
        cancelled|=StopSummon(id,ActionStopReason.Voluntary,sequence:sequence,execution:execution,tick:tick);
        return cancelled;
    }

    internal void ResetSummonSession(NetworkEntityId id)
    {_summonIngress.Remove(id);_summonResults.Remove(id);}
}
