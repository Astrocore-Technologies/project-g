using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal Action<uint>? PrepareActions {get;set;}
    internal Action<uint>? PrepareMotion {get;set;}
    internal Func<double,double>? MotionBoundary {get;set;}
    internal Func<NetworkEntityId,CombatMotion>? MotionFor {get;set;}
    internal Action<uint>? ResolveWorldContacts {get;set;}
    internal float MaximumTargetSpeed {get;set;}

    internal CombatMotion CollisionMotion(NetworkEntityId id)
    {
        var motion=MotionFor?.Invoke(id) ?? default;
        var actor=_actors[id];
        if(actor.Control!=CombatControlPhase.Airborne)return motion;
        var elapsed=_time-actor.FlightStarted;var duration=actor.FlightSeconds;
        return motion with {Velocity=motion.Velocity+System.Numerics.Vector3.UnitY*(float)(4*actor.FlightHeight/duration*(1-2*elapsed/duration)),
            Acceleration=motion.Acceleration-System.Numerics.Vector3.UnitY*(float)(8*actor.FlightHeight/(duration*duration))};
    }
    private uint? _completedFrame;
    private uint _simulationTick;
    internal bool CompletedFrame(uint tick)=>_completedFrame==tick;

    // The fixed world tick is still the only publication/checkpoint boundary. Internal
    // boundaries advance authoritative motion and combat together without clearing events.
    internal void SimulateFrame(float delta,uint tick,Action<double>? advanceMotion=null)
    {
        if(!float.IsFinite(delta) || delta<=0)throw new ArgumentException("Simulation delta must be finite and positive.");
        _simulationTick=tick;
        var end=_time+delta;
        if(!double.IsFinite(end) || end<=_time)throw new InvalidOperationException("Combat time cannot advance by this delta.");
        _events.Clear();_effectEvents.Clear();_hitContexts.Clear();_controlFeedback.Clear();
        InputAbilities?.BeginFrame(delta,_time);
        if(_receivedInputs.TryPeek(out _,out var initial)&&initial.At<=_time)
        {
            BeginContactBatch();
            var arrivals=DeliverReceivedInputs(tick);
            InputAbilities?.AdvanceFrame(_time,0,tick,arrivals);
            ResolveInputs(tick,acceptArrivals:arrivals);
            ResolveContactBatch();ExpireEffectsAtBoundary();
        }
        while(_time<end)
        {
            // AI decisions become common intents at their own authoritative boundary,
            // before calculating motion; player packet frequency cannot delay their start.
            if(PrepareActions is not null)
            {
                BeginContactBatch();
                PrepareActions(tick);
                InputAbilities?.ApplyQueuedCancellations(tick);
                ResolveInputs(tick,acceptArrivals:true,aiOnly:true);
                ResolveContactBatch();
            }
            PrepareMotion?.Invoke(tick);
            var at=NextBoundary(end);
            at=MotionBoundary?.Invoke(at) ?? at;
            at=InputAbilities?.NextBoundary(at) ?? at;
            var from=_time;var elapsed=at-from;
            if(elapsed>0)advanceMotion?.Invoke(elapsed);
            _time=at;
            InputAbilities?.SetTime(at);
            BeginContactBatch();
            AdvanceState(elapsed,from);
            var arrivals=DeliverReceivedInputs(tick);
            AdvanceBasicProjectiles(tick);
            InputAbilities?.AdvanceFrame(at,elapsed,tick,arrivals||at==end);
            AdvanceSummons(tick);
            AdvanceBasicActions(tick);
            ResolveWorldContacts?.Invoke(tick);
            ResolveInputs(tick,acceptArrivals:arrivals||at==end);
            ResolveContactBatch();
            ExpireEffectsAtBoundary();
        }
        InputAbilities?.PublishFrame(tick);
        _completedFrame=tick;
    }

    internal static double EarlierBoundary(double now,double horizon,double candidate)
        =>candidate>now && candidate<horizon ? candidate : horizon;

    private double NextBoundary(double end)
    {
        var next=end;
        foreach(var summon in _summons.Values)
            next=EarlierBoundary(_time,next,summon.Action.PhaseEndsAt);
        foreach(var execution in _basicActions.Values)
            if(!execution.Action.Finished)next=EarlierBoundary(_time,next,execution.Action.PhaseEndsAt);
        foreach(var (id,held) in _continuousBasic)
        {
            next=EarlierBoundary(_time,next,held.Expires);
            if(_actors.TryGetValue(id,out var actor))next=EarlierBoundary(_time,next,actor.ReadyAt);
        }
        if(_effectExpiries.Count>0)next=EarlierBoundary(_time,next,_effectExpiries.Min.At);
        foreach(var dodge in _dodges.Values)next=EarlierBoundary(_time,next,dodge.Until);
        foreach(var id in _controlled)
        {
            var actor=_actors[id];
            next=EarlierBoundary(_time,next,actor.ControlUntil);
            next=EarlierBoundary(_time,next,actor.RecoveryUntil);
            next=EarlierBoundary(_time,next,actor.HitCancelAt);
            foreach(var control in actor.Controls.Active)next=EarlierBoundary(_time,next,control.EndsAt);
        }
        foreach(var id in _defenseActive)
        {
            var guard=_defense[id];
            next=EarlierBoundary(_time,next,guard.ParryUntil);
            next=EarlierBoundary(_time,next,guard.ParryReadyAt);
            next=EarlierBoundary(_time,next,guard.GuardUntil);
            next=EarlierBoundary(_time,next,guard.GuardBreakUntil);
            next=EarlierBoundary(_time,next,guard.RecoverAt);
            if(guard.GuardUntil>_time && _catalog.Defense.BlockHoldPerSecond>0)
                next=EarlierBoundary(_time,next,_time+guard.Stamina/_catalog.Defense.BlockHoldPerSecond);
        }
        foreach(var id in _recoveringHealth)
            next=EarlierBoundary(_time,next,_time+_healthRecoveryInterval-_actors[id].HealthRecoveryElapsed);
        foreach(var id in _recoveringMana)
            next=EarlierBoundary(_time,next,_time+_manaRecoveryInterval-_actors[id].Resources.ManaRecoveryElapsed);
        foreach(var (id,input) in Inputs.Waiting)
        {
            // The receipt deadline is inclusive. Its next representable instant expires
            // the command even when no action boundary occurs before the next world tick.
            next=EarlierBoundary(_time,next,Math.BitIncrement(input.Expires));
            if(_actors.TryGetValue(id,out var actor))next=EarlierBoundary(_time,next,actor.ReadyAt);
        }
        return PlanBasicProjectiles(NextReceivedInput(next));
    }
}
