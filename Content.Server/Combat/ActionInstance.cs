using Content.Server.Data;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

internal enum ActionStopReason : byte { Completed, Voluntary, EnemyInterrupt, ResourceFailure, Death, Followup, Departure, Policy }

/// <summary>One ordinary execution. Activation is a deliberate barrier: callers validate/pay before releasing payload.</summary>
internal sealed class ActionInstance
{
    internal ActionInstance(ulong executionId,NetworkEntityId actor,uint inputSequence,string definitionId,
        ActionDefinition definition,ActionDurations durations,double now)
    {
        if(executionId==0 || !actor.IsValid || inputSequence==0 || !double.IsFinite(now) || now<0 ||
            !double.IsFinite(durations.Preparation) || durations.Preparation<0 || !double.IsFinite(durations.Active) || durations.Active<0 ||
            !double.IsFinite(durations.Recovery) || durations.Recovery<0)
            throw new ArgumentException("Invalid action execution identity, timing or clock.");
        ExecutionId=executionId; Actor=actor; InputSequence=inputSequence; DefinitionId=definitionId;
        Definition=definition; Durations=durations;
        Phase=durations.Preparation>0 ? CombatActionPhase.Preparation : CombatActionPhase.Activation;
        PhaseStartedAt=now; PhaseEndsAt=now+durations.Preparation;
    }

    internal ulong ExecutionId { get; }
    internal NetworkEntityId Actor { get; }
    internal uint InputSequence { get; }
    internal string DefinitionId { get; }
    internal ActionDefinition Definition { get; }
    internal ActionDurations Durations { get; }
    internal CombatActionPhase Phase { get; private set; }
    internal double PhaseStartedAt { get; private set; }
    internal double PhaseEndsAt { get; private set; }
    internal bool Activated { get; private set; }
    internal ActionStopReason? StopReason { get; private set; }
    internal bool Finished=>Phase==CombatActionPhase.Finished;
    internal float MovementMultiplier=>Finished ? 1 : PhaseDefinition.MovementMultiplier;
    internal bool CanTurn=>Finished || PhaseDefinition.CanTurn;
    internal ActionCancelPermission AllowedCancels=>Finished ? ActionCancelPermission.None : PhaseDefinition.Cancel;
    private ActionPhaseDefinition PhaseDefinition=>Phase switch
    {
        CombatActionPhase.Preparation or CombatActionPhase.Activation=>Definition.Preparation,
        CombatActionPhase.Active=>Definition.Active,
        _=>Definition.Recovery
    };

    internal bool CanCancel(ActionCancelPermission permission)=>!Finished && (PhaseDefinition.Cancel&permission)!=0;

    internal bool AwaitingActivation(double now)
    {
        if(Phase==CombatActionPhase.Preparation && Definition.Form!=CombatActionForm.Charged && now>=PhaseEndsAt) Phase=CombatActionPhase.Activation;
        return Phase==CombatActionPhase.Activation;
    }

    internal bool ReleaseCharge(double now)
    {
        if(Definition.Form!=CombatActionForm.Charged || Phase!=CombatActionPhase.Preparation || Activated)return false;
        Phase=CombatActionPhase.Activation;PhaseEndsAt=now;return true;
    }

    internal bool Activate(double now)
    {
        if(!AwaitingActivation(now) || Activated) return false;
        Activated=true; Phase=CombatActionPhase.Active; PhaseStartedAt=now; PhaseEndsAt=now+Durations.Active;
        return true;
    }

    internal void BeginRecovery(double now,double? duration=null)
    {
        if(Finished || !Activated) return;
        Phase=CombatActionPhase.Recovery; PhaseStartedAt=now; PhaseEndsAt=now+(duration??Durations.Recovery);
        if(PhaseEndsAt<=now) Complete();
    }

    internal void Advance(double now)
    {
        if(Phase==CombatActionPhase.Active && now>=PhaseEndsAt) BeginRecovery(PhaseEndsAt);
        if(Phase==CombatActionPhase.Recovery && now>=PhaseEndsAt) Complete();
    }

    internal bool Stop(ActionStopReason reason,ActionCancelPermission permission=ActionCancelPermission.Voluntary)
    {
        if(Finished || reason==ActionStopReason.Completed ||
            reason==ActionStopReason.Voluntary && !CanCancel(permission) ||
            reason==ActionStopReason.EnemyInterrupt && !Definition.Interruptible) return false;
        StopReason=reason; Phase=CombatActionPhase.Finished; return true;
    }

    internal double ResourceFractionForStop => Activated ? 0 : StopReason is ActionStopReason.EnemyInterrupt or ActionStopReason.ResourceFailure or ActionStopReason.Death
        ? Definition.Form is CombatActionForm.Cast or CombatActionForm.Charged ? .5 : 0 : 0;

    private void Complete() { Phase=CombatActionPhase.Finished; StopReason=ActionStopReason.Completed; }
}
