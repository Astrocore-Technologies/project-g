using System.Numerics;
using Content.Shared.Combat;
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

    private void InterruptControl(NetworkEntityId id,bool stopMotion=true)
    {
        StopContinuousBasic(id);
        InterruptRequested?.Invoke(id); if(stopMotion) StopForControl?.Invoke(id);
        _landingRecover.Remove(id);
        if (_defense.TryGetValue(id, out var defense))
        { defense.GuardUntil = defense.ParryUntil = 0; _defenseDirty.Add(id); }
    }

    internal bool Knockup(NetworkEntityId id, double seconds, float height, double downSeconds,
        NetworkEntityId source=default,ushort definition=0,ulong execution=0,ReleasedSourceSnapshot? releasedSource=null)
    {
        var actor = _actors[id];
        // A second launch cannot restart a flight; a recovery roll still permits opposing control.
        if (actor.Control is not (CombatControlPhase.None or CombatControlPhase.Recovering) || !actor.CanBeStunned || actor.Health<=0) return false;
        var clippedHeight=height;
        if (_navigation.Visibility is { } visibility)
        {
            var head=actor.Foot+Vector3.UnitY*(_navigation.Surface?.Map.AgentHeight ?? 2);
            var lo=0f; var hi=1f;
            for(var i=0;i<14;i++) { var mid=(lo+hi)/2; if(visibility.Clear(head,head+Vector3.UnitY*height*mid))lo=mid;else hi=mid; }
            clippedHeight*=lo;
        }
        if (clippedHeight <= .001f) return false;
        var resistance=actor.ControlResistance.Factor(CrowdControlKind.Knockup);
        if(resistance<=0) return false;
        var exposure=actor.Controls.Apply(new(releasedSource?.Identity ?? SourceIdentity(source),definition,CrowdControlKind.Knockup),execution==0 ? AllocateExecution() : execution,
            (seconds+downSeconds)*resistance,1,_time);
        if(!exposure.Accepted) return false;
        RecordJournal(source,id,CombatJournalKind.Control,duration:exposure.Duration,strength:1,skill:definition,control:CrowdControlKind.Knockup);
        var factor=exposure.Duration/(seconds+downSeconds);
        _controlled.Add(id); _defenseDirty.Add(id);
        InterruptControl(id);
        actor.Control = CombatControlPhase.Airborne; actor.FlightStarted = _time;
        actor.FlightSeconds = seconds * factor; actor.ControlUntil = _time + actor.FlightSeconds;
        actor.KnockdownSeconds = downSeconds * factor;
        // Clip the trajectory beneath a ceiling using the same server collision contract as attacks.
        actor.FlightHeight = clippedHeight;
        ReportHostileContact(source,id);
        return true;
    }

    internal bool Knockback(NetworkEntityId id, Vector2 direction, float distance, float speed,
        NetworkEntityId source=default,ushort definition=0,ulong execution=0,ReleasedSourceSnapshot? releasedSource=null)
        => Displace(id,direction,distance,speed,CrowdControlKind.Knockback,source,definition,execution,releasedSource);

    internal bool Pull(NetworkEntityId id,Vector2 origin,float distance,float speed,NetworkEntityId source=default,ushort definition=0,ulong execution=0,
        ReleasedSourceSnapshot? releasedSource=null)
    {
        var offset=origin-_actors[id].Position; var length=offset.Length();
        return length>.0001f && Displace(id,offset/length,MathF.Min(distance,length),speed,CrowdControlKind.Pull,source,definition,execution,releasedSource);
    }

    private bool Displace(NetworkEntityId id,Vector2 direction,float distance,float speed,CrowdControlKind kind,
        NetworkEntityId source,ushort definition,ulong execution,ReleasedSourceSnapshot? releasedSource=null)
    {
        var actor = _actors[id];
        if (actor.Control is not (CombatControlPhase.None or CombatControlPhase.Recovering) || !actor.CanBeStunned || actor.Health <= 0 ||
            !SurfaceDash.TryDestination(_navigation, actor.Foot, direction, distance, out var destination, out _)) return false;
        var resistance=actor.ControlResistance.Factor(kind);
        var baseDuration=Vector2.Distance(actor.Position,destination)/speed*resistance;
        if(resistance<=0 || baseDuration<=0) return false;
        var quote=actor.Controls.Ledger.QuoteDuration(ControlCategory.Movement,baseDuration,_time);
        if(!quote.Accepted || !SurfaceDash.TryDestination(_navigation,actor.Foot,direction,(float)quote.Duration*speed,out destination,out _)) return false;
        if (StartControlDash?.Invoke(id, destination, speed) != true) return false;
        // Motion is already validated and accepted; interruption must not reset that newly created dash.
        InterruptControl(id,stopMotion:false);
        var exposure=actor.Controls.Apply(new(releasedSource?.Identity ?? SourceIdentity(source),definition,kind),execution==0 ? AllocateExecution() : execution,baseDuration,1,_time);
        _controlled.Add(id); _defenseDirty.Add(id);
        actor.Control = CombatControlPhase.Displaced;
        actor.ControlUntil = _time + exposure.Duration;
        RecordJournal(source,id,CombatJournalKind.Control,duration:exposure.Duration,strength:1,skill:definition,control:kind);
        ReportHostileContact(source,id);
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
        actor.Controls.Remove(CrowdControlKind.Knockup,_time); actor.Controls.Remove(CrowdControlKind.Knockdown,_time);
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
            actor.Controls.Advance(_time); RefreshControlModifiers(actor); RefreshHardControl(actor); UpdateFear(actor);
            if (actor.Health <= 0) { actor.Control = CombatControlPhase.None; actor.AirOffset = 0; _landingRecover.Remove(id); }
            if (actor.Control == CombatControlPhase.Airborne)
            {
                var progress = (float)Math.Clamp((_time - actor.FlightStarted) / actor.FlightSeconds, 0, 1);
                actor.AirOffset = actor.FlightHeight * 4 * progress * (1 - progress);
                if (_time >= actor.ControlUntil)
                {
                    actor.AirOffset = 0; actor.Control = CombatControlPhase.KnockedDown;
                    actor.ControlUntil = actor.FlightStarted+actor.FlightSeconds+actor.KnockdownSeconds;
                    if (_landingRecover.Remove(id, out var command) && _defense.TryGetValue(id, out var defense))
                    { defense.Outcome = QuickRecover(id, command); _defenseDirty.Add(id); }
                }
            }
            if (actor.Control != CombatControlPhase.None && actor.Control!=CombatControlPhase.Airborne && _time >= actor.ControlUntil)
                actor.Control = CombatControlPhase.None;
            if (_defense.ContainsKey(id)) _defenseDirty.Add(id);
            if (actor.Control == CombatControlPhase.None && actor.RecoveryUntil <= _time &&
                !actor.Controls.HasActive && actor.StunnedUntil <= _time) _controlExpired.Add(id);
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
        var acr=new SavedAcrState[5];
        for(var category=ControlCategory.Hard;category<=ControlCategory.Targeting;category++)
        {
            var snapshot=actor.Controls.Ledger.Snapshot(category,_time);
            acr[(int)category]=new(category,snapshot.Used,snapshot.ResetInSeconds);
        }
        return new(1,Math.Max(0,down),Math.Max(0,actor.StunnedUntil-_time),RecoveryRemaining(id),0,0,acr,InstancesSaved:true,Phase:actor.Control);
    }
    internal void RestoreControl(NetworkEntityId id, SavedCombatControl? saved, double offline, bool restoreSummary=true,bool finishForcedMotion=false)
    {
        if (saved is null) return; saved.Validate(); var actor = _actors[id];
        restoreSummary &= !saved.InstancesSaved;
        actor.StunnedUntil = _time + Math.Max(0,saved.StunSeconds-offline);
        if(restoreSummary) actor.Controls.RestoreSummary(CrowdControlKind.Stun,Math.Max(0,saved.StunSeconds-offline),_time);
        actor.RecoveryUntil = _time + Math.Max(0,saved.RecoverySeconds-offline);
        if(saved.Acr is {} acr)
            foreach(var state in acr) actor.Controls.Ledger.Restore(new(state.Category,state.Used,state.ResetSeconds),_time,offline);
        var finished=finishForcedMotion && saved.Phase is CombatControlPhase.Airborne or CombatControlPhase.Displaced;
        if(finished && saved.DownSeconds>offline) actor.Controls.CategoryEndedByRestart(saved.Phase==CombatControlPhase.Airborne ? ControlCategory.Hard : ControlCategory.Movement,_time);
        if (saved.DownSeconds > offline && !finished)
        {
            actor.Control = CombatControlPhase.KnockedDown; actor.ControlUntil = _time+saved.DownSeconds-offline;
            if(restoreSummary) actor.Controls.RestoreSummary(CrowdControlKind.Knockdown,saved.DownSeconds-offline,_time);
        }
        _controlled.Add(id);
    }
    private void ClearControl(NetworkEntityId id)
    {
        var a = _actors[id]; a.Control = CombatControlPhase.None; a.AirOffset = 0;
        a.Controls.Clear(_time);
        a.RecoveryUntil = 0; a.HitFollowups = []; _landingRecover.Remove(id); _controlled.Remove(id);
    }
}
