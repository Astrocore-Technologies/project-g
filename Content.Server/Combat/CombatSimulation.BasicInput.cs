using System.Numerics;
using Content.Server.Stats;
using Content.Shared.Combat;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private sealed class ContinuousBasicInput(AttackCommand command, double expires)
    {
        public AttackCommand Command = command;
        public double Expires = expires;
    }

    private readonly Dictionary<NetworkEntityId, ContinuousBasicInput> _continuousBasic = new();
    private readonly List<NetworkEntityId> _activeBasicInputs = new();
    private readonly HashSet<NetworkEntityId> _suppressedBasicInputs = new();
    private readonly double _continuousBasicLease;
    private ulong _nextExecution = 1;
    internal bool ExecutionCapacityAvailable=>_nextExecution!=0;

    internal bool HasContinuousBasic(NetworkEntityId id) => _continuousBasic.ContainsKey(id);
    internal void StopContinuousBasic(NetworkEntityId id) => _continuousBasic.Remove(id);
    internal void NotifyNavigationInput(NetworkEntityId id, uint order)
    {
        var actor = _actors[id];
        actor.LastNavigationOrder = order;
        if (_continuousBasic.TryGetValue(id, out var held) && !MovementSimulation.IsSequenceNewer(held.Command.InputOrder, order))
            StopContinuousBasic(id);
    }
    private static bool StaleContinuousInput(Combatant actor, AttackCommand command) =>
        command.Mode is BasicAttackMode.BeginContinuous or BasicAttackMode.UpdateContinuous &&
        actor.LastNavigationOrder != 0 && !MovementSimulation.IsSequenceNewer(command.InputOrder, actor.LastNavigationOrder);
    internal void SuppressBasicThisTick(NetworkEntityId id) => _suppressedBasicInputs.Add(id);

    internal ulong AllocateExecution()
    {
        if (_nextExecution == 0) throw new InvalidOperationException("Combat execution IDs exhausted.");
        return _nextExecution++;
    }

    public bool Queue(NetworkEntityId id, AttackCommand command, uint serverTick)
    {
        if (!_actors.TryGetValue(id, out var actor) || actor.Kind != CombatEntityKind.Player || !actor.Active ||
            command.Sequence == 0 || !MovementSimulation.IsSequenceNewer(command.Sequence, actor.LastSequence))
            return false;
        actor.LastSequence = command.Sequence;
        if (!BasicAttackShape.IsValidDirection3D(command.Direction,command.DirectionY) || !Enum.IsDefined(command.Mode))
        {
            _results[id] = new(command.Sequence, serverTick, AttackOutcome.InvalidDirection);
            return false;
        }
        // Key-up survives a same-tick press/flood. A quick tap retains its one
        // deliberate swing, but cannot leave a repeating input behind.
        if (command.Mode == BasicAttackMode.EndContinuous)
        { StopContinuousBasic(id); return true; }
        if (StaleContinuousInput(actor, command))
        { _results[id] = new(command.Sequence, serverTick, AttackOutcome.InvalidState); return false; }
        if (!DeliveringReceivedInput && actor.LastRequestTick == serverTick)
        {
            _results[id] = new(command.Sequence, serverTick, AttackOutcome.RateLimited);
            return false;
        }
        actor.LastRequestTick = serverTick;
        if (command.Mode == BasicAttackMode.UpdateContinuous)
        {
            if (_continuousBasic.TryGetValue(id, out var held))
            {
                // Preserve the start's acknowledgement sequence. Updates never
                // create/restart a hold; executions have independent server IDs.
                held.Command = held.Command with { Direction = command.Direction,DirectionY=command.DirectionY, ClientTick = command.ClientTick };
                held.Expires = _time + _continuousBasicLease;
                UpdateBasicAim(id,held.Command.Sequence,0,command.Direction,command.DirectionY);
            }
            return true;
        }
        StopContinuousBasic(id);
        if (command.Mode == BasicAttackMode.BeginContinuous)
            _continuousBasic.Add(id, new(command, _time + _continuousBasicLease));
        Inputs.Submit(id, new(OrdinaryInputKind.Basic, command, default, default, 0, InputDeadline, 0,
            Capabilities:actor.Weapon.Capabilities, Invalidated:IsStunned(id) || actor.Control!=CombatControlPhase.None || BlocksAction(id,actor.Weapon.Capabilities)));
        return true;
    }

    private InputDisposition ExecuteBasicInput(NetworkEntityId id, AttackCommand command, uint tick)
    {
        var actor = _actors[id];
        if (StaleContinuousInput(actor, command))
        { _results[id] = new(command.Sequence, tick, AttackOutcome.InvalidState); return InputDisposition.Rejected; }
        if (actor.Health <= 0 || !actor.Active || !AllowsCompanionAction(id,0) || IsStunned(id) || actor.Control != CombatControlPhase.None ||
            BlocksAction(id, actor.Weapon.Capabilities) || _defense.TryGetValue(id, out var defense) && !defense.Connected)
        { _results[id] = new(command.Sequence, tick, AttackOutcome.InvalidState); StopContinuousBasic(id); return InputDisposition.Rejected; }
        var canCancel = actor.Action?.CanCancel(ActionCancelPermission.BasicAttack) == true;
        if (_time < actor.ReadyAt || !BasicStateAvailable(id, actor) && !canCancel)
        {
            // A held input owns its lease, not a second waiting-command slot.
            if (command.Mode == BasicAttackMode.BeginContinuous)
            {
                if (!BasicStateAvailable(id, actor))
                { _results[id] = new(command.Sequence, tick, AttackOutcome.InvalidState); StopContinuousBasic(id); return InputDisposition.Rejected; }
                _results[id] = new(command.Sequence, tick, AttackOutcome.Accepted); return InputDisposition.Executed;
            }
            _results[id] = new(command.Sequence, tick, AttackOutcome.Buffered); return InputDisposition.Waiting;
        }
        if (canCancel)
        {
            if (!CancelOrdinaryAction(id, ActionCancelPermission.BasicAttack))
            { _results[id] = new(command.Sequence, tick, AttackOutcome.Buffered); return InputDisposition.Waiting; }
            SetRecovery(id, 0);
            // Cancellation can finish a deferred weapon break and replace the attack profile.
            return ExecuteBasicInput(id, command, tick);
        }
        _results[id] = new(command.Sequence, tick, AttackOutcome.Accepted);
        PlayerAction?.Invoke(id); StartBasic(actor,command,tick);
        return InputDisposition.Executed;
    }

    internal bool QueueAiBasic(NetworkEntityId id,AttackCommand command,uint tick)
    {
        if(!_actors.TryGetValue(id,out var actor) || !IsAiUnit(actor) || !actor.Active || actor.Health<=0 || command.Sequence==0 ||
            !MovementSimulation.IsSequenceNewer(command.Sequence,actor.LastSequence) ||
            command.Mode is not (BasicAttackMode.Manual or BasicAttackMode.BeginContinuous) ||
            !BasicAttackShape.IsValidDirection3D(command.Direction,command.DirectionY))return false;
        actor.LastSequence=command.Sequence;
        // Server AI revalidates this intent at every motion boundary. It has no client heartbeat lease.
        if(command.Mode==BasicAttackMode.BeginContinuous)_continuousBasic[id]=new(command,double.PositiveInfinity);
        Inputs.Submit(id,new(OrdinaryInputKind.Basic,command,default,default,0,InputDeadline,0,Capabilities:actor.Weapon.Capabilities,
            Invalidated:IsStunned(id) || actor.Control!=CombatControlPhase.None || BlocksAction(id,actor.Weapon.Capabilities)));
        return true;
    }
    internal void UpdateAiBasicIntent(NetworkEntityId id,Vector2 direction,float directionY)
    {
        if(!_actors.TryGetValue(id,out var actor)||!IsAiUnit(actor)||!_continuousBasic.TryGetValue(id,out var held)||
            !double.IsPositiveInfinity(held.Expires)||!BasicAttackShape.IsValidDirection3D(direction,directionY))return;
        held.Command=held.Command with {Direction=direction,DirectionY=directionY};
        UpdateBasicAim(id,held.Command.Sequence,0,direction,directionY);
    }
    internal void StopAiBasicIntent(NetworkEntityId id,uint sequence)
    {
        if(!_actors.TryGetValue(id,out var actor)||!IsAiUnit(actor))return;
        StopContinuousBasic(id);Inputs.CancelBasic(id,sequence);
    }

    private bool BasicStateAvailable(NetworkEntityId id, Combatant actor) =>
        actor.Health > 0 && actor.Active && AllowsCompanionAction(id,0) && actor.Action is not {Finished:false} && !IsActionLocked(id) &&
        !IsDefending(id) &&
        !BlocksAction(id,actor.Weapon.Capabilities) &&
        (!_defense.TryGetValue(id, out var defense) || defense.Connected);

    private void SimulateContinuousBasic(uint tick,bool aiOnly=false)
    {
        _activeBasicInputs.Clear();
        _activeBasicInputs.AddRange(_continuousBasic.Keys);
        // Snapshot only active IDs: a damage callback may stop another hold.
        foreach (var id in _activeBasicInputs)
        {
            if (!_continuousBasic.TryGetValue(id, out var held)) continue;
            if (!_actors.TryGetValue(id, out var actor) || held.Expires <= _time)
            { _continuousBasic.Remove(id); continue; }
            if(aiOnly&&actor.Kind==CombatEntityKind.Player)continue;
            if(OwnsBasicAction(id,actor))continue;
            if(!BasicStateAvailable(id,actor)){_continuousBasic.Remove(id);continue;}
            if (Inputs.HasBasicArrival(id) || _suppressedBasicInputs.Contains(id) || _time < actor.ReadyAt) continue;
            PlayerAction?.Invoke(id);
            StartBasic(actor,held.Command,tick);
        }
    }
}
