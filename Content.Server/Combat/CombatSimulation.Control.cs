using System.Numerics;
using Content.Server.Persistence;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly HashSet<NetworkEntityId> _controlled = new();
    private readonly List<NetworkEntityId> _controlExpired = new();
    private readonly Dictionary<NetworkEntityId, DefenseCommand> _landingRecover = new();
    internal Action<NetworkEntityId>? StopForControl { get; set; }
    internal Func<NetworkEntityId, Vector2, float, bool>? StartControlDash { get; set; }
    internal bool IsActionLocked(NetworkEntityId id) => _actors.TryGetValue(id, out var a) &&
        (IsStunned(id) || a.Control != CombatControlPhase.None || a.RecoveryUntil > _time);
    internal double RecoveryRemaining(NetworkEntityId id) => Math.Max(0, _actors[id].RecoveryUntil - _time);
    internal bool CanHitFollowup(NetworkEntityId id, string skill) => _actors.TryGetValue(id, out var a) &&
        a.RecoveryUntil > _time && _time >= a.HitCancelAt && a.HitFollowups.Contains(skill);

    internal void SetRecovery(NetworkEntityId id, double seconds, double cancelAfter = 0, string[]? followups = null)
    {
        var actor = _actors[id]; actor.RecoveryUntil = _time + seconds;
        actor.HitCancelAt = _time + cancelAfter; actor.HitFollowups = followups ?? [];
        if (seconds > 0) { _controlled.Add(id); _defenseDirty.Add(id); }
    }

    // Every source consumes the same target-owned counter. Rejected attempts cannot prolong immunity.
    private double ControlFactor(Combatant actor, bool commit=true)
    {
        if (!actor.CanBeStunned || actor.Health <= 0) return 0;
        var count=_time >= actor.ControlResetAt ? 0 : actor.ControlCount;
        if (count >= 2) return 0;
        var factor = count == 0 ? 1 : _catalog.Defense.RepeatedControlFactor;
        if (!commit) return factor;
        actor.ControlCount=count+1;
        actor.ControlResetAt = _time + _catalog.Defense.ControlResetSeconds;
        _controlled.Add(actor.Id); _defenseDirty.Add(actor.Id);
        return factor;
    }

    private void InterruptControl(NetworkEntityId id)
    {
        InterruptRequested?.Invoke(id); StopForControl?.Invoke(id);
        _landingRecover.Remove(id);
        if (_defense.TryGetValue(id, out var defense))
        { defense.GuardUntil = defense.ParryUntil = 0; _defenseDirty.Add(id); }
    }

    internal bool Knockup(NetworkEntityId id, double seconds, float height, double downSeconds)
    {
        var actor = _actors[id];
        // A second launch cannot restart a flight; a recovery roll still permits opposing control.
        if (actor.Control is not (CombatControlPhase.None or CombatControlPhase.Recovering)) return false;
        var clippedHeight=height;
        if (_navigation.Visibility is { } visibility)
        {
            var head=actor.Foot+Vector3.UnitY*(_navigation.Surface?.Map.AgentHeight ?? 2);
            var lo=0f; var hi=1f;
            for(var i=0;i<14;i++) { var mid=(lo+hi)/2; if(visibility.Clear(head,head+Vector3.UnitY*height*mid))lo=mid;else hi=mid; }
            clippedHeight*=lo;
        }
        if (clippedHeight <= .001f) return false;
        var factor = ControlFactor(actor); if (factor == 0) return false;
        InterruptControl(id);
        actor.Control = CombatControlPhase.Airborne; actor.FlightStarted = _time;
        actor.FlightSeconds = seconds * factor; actor.ControlUntil = _time + actor.FlightSeconds;
        actor.KnockdownSeconds = downSeconds * factor;
        // Clip the trajectory beneath a ceiling using the same server collision contract as attacks.
        actor.FlightHeight = clippedHeight;
        return true;
    }

    internal bool Knockback(NetworkEntityId id, Vector2 direction, float distance, float speed)
    {
        var actor = _actors[id];
        if (actor.Control is not (CombatControlPhase.None or CombatControlPhase.Recovering) || !actor.CanBeStunned || actor.Health <= 0 ||
            !SurfaceDash.TryDestination(_navigation, actor.Foot, direction, distance, out var destination, out _)) return false;
        var factor = ControlFactor(actor,commit:false); if (factor == 0) return false;
        if (!SurfaceDash.TryDestination(_navigation, actor.Foot, direction, distance * (float)factor, out destination, out _)) return false;
        InterruptControl(id);
        if (StartControlDash?.Invoke(id, destination, speed) != true) return false;
        ControlFactor(actor);
        actor.Control = CombatControlPhase.Displaced;
        actor.ControlUntil = _time + Vector2.Distance(actor.Position, destination) / speed;
        return true;
    }

    internal DefenseOutcome QuickRecover(NetworkEntityId id, DefenseCommand command)
    {
        var actor = _actors[id]; var defense = _defense[id]; var balance = _catalog.Defense;
        if (actor.Health <= 0 || !actor.Active || !defense.Connected || IsStunned(id)) return DefenseOutcome.InvalidState;
        if (defense.QuickReadyAt > _time) return DefenseOutcome.Cooldown;
        if (defense.Stamina < balance.QuickRecoverCost) return DefenseOutcome.NoStamina;
        if (actor.Control == CombatControlPhase.Airborne && actor.ControlUntil - _time <= balance.InputBufferSeconds)
        { _landingRecover[id] = command; return DefenseOutcome.Accepted; }
        if (actor.Control != CombatControlPhase.KnockedDown ||
            !SurfaceDash.TryDestination(_navigation, new(actor.Position.X,actor.Height,actor.Position.Y), command.Direction,
                balance.QuickRecoverRange, out var destination, out _) ||
            StartControlDash?.Invoke(id, destination, balance.QuickRecoverSpeed) != true) return DefenseOutcome.InvalidState;
        Spend(id, defense, balance.QuickRecoverCost); defense.QuickReadyAt = _time + balance.QuickRecoverCooldown;
        defense.GuardUntil = defense.ParryUntil = 0;
        actor.Control = CombatControlPhase.Recovering; actor.AirOffset = 0;
        actor.ControlUntil = _time + Vector2.Distance(actor.Position, destination) / balance.QuickRecoverSpeed;
        _landingRecover.Remove(id); PlayerAction?.Invoke(id);
        return DefenseOutcome.Accepted;
    }

    private void SimulateControl()
    {
        _controlExpired.Clear();
        foreach (var id in _controlled)
        {
            var actor = _actors[id];
            if (actor.Health <= 0) { actor.Control = CombatControlPhase.None; actor.AirOffset = 0; _landingRecover.Remove(id); }
            if (actor.Control == CombatControlPhase.Airborne)
            {
                var progress = (float)Math.Clamp((_time - actor.FlightStarted) / actor.FlightSeconds, 0, 1);
                actor.AirOffset = actor.FlightHeight * 4 * progress * (1 - progress);
                if (_time >= actor.ControlUntil)
                {
                    actor.AirOffset = 0; actor.Control = CombatControlPhase.KnockedDown;
                    actor.ControlUntil = _time + actor.KnockdownSeconds;
                    if (_landingRecover.Remove(id, out var command) && _defense.TryGetValue(id, out var defense))
                    { defense.Outcome = QuickRecover(id, command); _defenseDirty.Add(id); }
                }
            }
            else if (actor.Control != CombatControlPhase.None && _time >= actor.ControlUntil)
                actor.Control = CombatControlPhase.None;
            if (_defense.ContainsKey(id)) _defenseDirty.Add(id);
            if (actor.Control == CombatControlPhase.None && actor.RecoveryUntil <= _time &&
                actor.ControlResetAt <= _time && actor.StunnedUntil <= _time) _controlExpired.Add(id);
        }
        foreach (var id in _controlExpired) _controlled.Remove(id);
    }

    internal EntitySnapshot ControlSnapshot(EntitySnapshot snapshot)
    {
        if (!_actors.TryGetValue(snapshot.EntityId, out var actor)) return snapshot;
        return snapshot with { AirOffset = actor.AirOffset, Control = actor.Control,
            ControlRemaining = actor.Control == CombatControlPhase.None ? 0 : (float)Math.Max(0,actor.ControlUntil - _time) };
    }

    internal SavedCombatControl CaptureControl(NetworkEntityId id)
    {
        var actor = _actors[id];
        // Persist a grounded restriction instead of an invalid airborne navigation position.
        var down = actor.Control == CombatControlPhase.Airborne ? actor.ControlUntil - _time + actor.KnockdownSeconds
            : actor.Control != CombatControlPhase.None ? actor.ControlUntil - _time : 0;
        return new(1,Math.Max(0,down),Math.Max(0,actor.StunnedUntil-_time),RecoveryRemaining(id),
            actor.ControlCount,Math.Max(0,actor.ControlResetAt-_time));
    }
    internal void RestoreControl(NetworkEntityId id, SavedCombatControl? saved, double offline)
    {
        if (saved is null) return; saved.Validate(); var actor = _actors[id];
        actor.StunnedUntil = _time + Math.Max(0,saved.StunSeconds-offline);
        actor.RecoveryUntil = _time + Math.Max(0,saved.RecoverySeconds-offline);
        actor.ControlCount = saved.Count; actor.ControlResetAt = _time + Math.Max(0,saved.ResetSeconds-offline);
        if (saved.DownSeconds > offline) { actor.Control = CombatControlPhase.KnockedDown; actor.ControlUntil = _time+saved.DownSeconds-offline; }
        _controlled.Add(id);
    }
    private void ClearControl(NetworkEntityId id)
    {
        var a = _actors[id]; a.Control = CombatControlPhase.None; a.AirOffset = 0;
        a.RecoveryUntil = 0; a.HitFollowups = []; _landingRecover.Remove(id); _controlled.Remove(id);
    }
}
