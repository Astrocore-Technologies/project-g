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
        if (!BasicAttackShape.IsValidDirection(command.Direction) || !Enum.IsDefined(command.Mode))
        {
            _results[id] = new(command.Sequence, serverTick, AttackOutcome.InvalidDirection);
            return false;
        }
        // Key-up survives a same-tick press/flood. A quick tap retains its one
        // deliberate swing, but cannot leave a repeating input behind.
        if (command.Mode == BasicAttackMode.EndContinuous)
        { StopContinuousBasic(id); return true; }
        if (actor.LastRequestTick == serverTick)
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
                held.Command = held.Command with { Direction = command.Direction, ClientTick = command.ClientTick };
                held.Expires = _time + _continuousBasicLease;
            }
            return true;
        }
        StopContinuousBasic(id);
        if (command.Mode == BasicAttackMode.BeginContinuous)
            _continuousBasic.Add(id, new(command, _time + _continuousBasicLease));
        AttemptAction(id);
        _pending[id] = command;
        return true;
    }

    private bool BasicStateAvailable(NetworkEntityId id, Combatant actor) =>
        actor.Health > 0 && actor.Active && !actor.IsCasting && !IsActionLocked(id) &&
        !IsDefending(id) &&
        !BlocksAction(id,actor.Weapon.Capabilities) &&
        (!_defense.TryGetValue(id, out var defense) || defense.Connected);

    private void SimulateContinuousBasic(uint tick)
    {
        _activeBasicInputs.Clear();
        _activeBasicInputs.AddRange(_continuousBasic.Keys);
        // Snapshot only active IDs: a damage callback may stop another hold.
        foreach (var id in _activeBasicInputs)
        {
            if (!_continuousBasic.TryGetValue(id, out var held)) continue;
            if (!_actors.TryGetValue(id, out var actor) || held.Expires <= _time || !BasicStateAvailable(id, actor))
            { _continuousBasic.Remove(id); continue; }
            if (_pending.ContainsKey(id) || _suppressedBasicInputs.Contains(id) || _time < actor.ReadyAt) continue;
            actor.ReadyAt = StatMath.Add(_time, actor.AttackInterval);
            PlayerAction?.Invoke(id);
            Resolve(actor, held.Command, tick);
        }
    }
}
