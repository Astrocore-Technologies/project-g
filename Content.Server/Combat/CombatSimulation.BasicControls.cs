using Content.Shared.Combat;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly Dictionary<NetworkEntityId,BasicControlCommand> _basicControls=new();

    public BasicInputState BasicInputState(NetworkEntityId id,uint tick)
    {var actor=_actors[id];return new(id,tick,actor.LastSequence,actor.LastBasicControlSequence);}

    public bool QueueBasicControl(NetworkEntityId id,BasicControlCommand command,uint tick)
    {
        if(!_actors.TryGetValue(id,out var actor) || actor.Kind!=CombatEntityKind.Player || !actor.Active || actor.Health<=0 ||
            command.Sequence==0 || command.ActionSequence==0 || !Enum.IsDefined(command.Kind) ||
            !BasicAttackShape.IsValidDirection3D(command.Direction,command.DirectionY) || !MovementSimulation.IsSequenceNewer(command.Sequence,actor.LastBasicControlSequence))return false;
        actor.LastBasicControlSequence=command.Sequence;
        if(command.Kind==BasicControlKind.Aim&&_basicControls.TryGetValue(id,out var terminal)&&terminal.Kind==BasicControlKind.Cancel&&
            terminal.ActionSequence==command.ActionSequence&&(terminal.ExecutionId==command.ExecutionId||terminal.ExecutionId==0||command.ExecutionId==0))return false;
        // One control per tick; a deliberate cancel may still supersede that tick's aim.
        if(!DeliveringReceivedInput && actor.LastBasicControlTick==tick && !(command.Kind==BasicControlKind.Cancel &&
            _basicControls.TryGetValue(id,out var previous) && previous.Kind==BasicControlKind.Aim))return false;
        actor.LastBasicControlTick=tick;
        _basicControls[id]=command;return true;
    }

    private void ApplyBasicControls(uint tick)
    {
        foreach(var (id,command) in _basicControls)
        {
            if(!_actors.ContainsKey(id))continue;
            if(command.Kind==BasicControlKind.Aim)
            {UpdateBasicAim(id,command.ActionSequence,command.ExecutionId,command.Direction,command.DirectionY);continue;}
            // A nonzero server identity cannot address a queued input or another execution.
            var cancelled=command.ExecutionId==0 && Inputs.CancelBasic(id,command.ActionSequence);
            cancelled|=StopBasic(id,ActionStopReason.Voluntary,inputSequence:command.ActionSequence,executionId:command.ExecutionId);
            if(command.ExecutionId==0 && _continuousBasic.TryGetValue(id,out var held) && held.Command.Sequence==command.ActionSequence &&
                !_basicActions.ContainsKey(id))
            {StopContinuousBasic(id);cancelled=true;}
            _results[id]=new(command.ActionSequence,tick,cancelled ? AttackOutcome.Cancelled : AttackOutcome.CancelDenied);
        }
        _basicControls.Clear();
    }
}
