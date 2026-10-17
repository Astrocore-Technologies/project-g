using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Shared.Combat;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.World;

public enum NpcBehavior { Idle, Chasing, Windup, Returning, Defeated }

/// <summary>One bounded prototype NPC. Perception/path decisions run at lower frequency than movement.</summary>
public sealed class NpcSimulation
{
    private readonly CombatSimulation _combat;
    private readonly SpatialIndex _spatial;
    private readonly NavigationGrid _grid;
    private readonly NpcOptions _options;
    private readonly HashSet<NetworkEntityId> _candidates = new();
    private double _decisionRemaining;
    private double _windupRemaining;
    private Vector2 _direction;
    private uint _sequence;
    private double _time;
    private readonly AbilityDefinition? _area;
    private readonly BossOptions? _boss;
    private readonly AbilitySimulation? _abilities;
    private bool _nextArea;
    private bool _castingArea;
    private uint _abilitySequence;
    private bool _abilityStarted;
    private ulong _basicExecution;
    private bool _waitingForAttack;

    public NpcSimulation(NetworkEntityId id, CombatSimulation combat, SpatialIndex spatial, NavigationGrid grid,
        MovementSettings movement, NpcOptions options, float cellSize, AbilityDefinition? area = null,
        BossOptions? boss = null, StatCalculator? calculator = null, AbilitySimulation? abilities = null)
    {
        if (!float.IsFinite(options.Speed) || options.Speed is <= 0 or > 30 ||
            !float.IsFinite(options.AggroRadius) || options.AggroRadius <= 0 ||
            !float.IsFinite(options.LeashRadius) || options.LeashRadius < options.AggroRadius || options.LeashRadius > cellSize * 32 ||
            !float.IsFinite(options.DecisionSeconds) || options.DecisionSeconds is < 0.1f or > 2 ||
            !float.IsFinite(options.WindupSeconds) || options.WindupSeconds is < 0.2f or > 10)
            throw new ArgumentException("Invalid bounded NPC settings.");
        Id = id; _combat = combat; _spatial = spatial; _grid = grid; _options = options;
        Home = combat.Get(id).Position;
        _combat.ConfigureNpcEncounter(id,Home,options.LeashRadius);
        Motion = new NavigationMover(grid, movement with { Speed = options.Speed }, new NavigationPathfinder(grid), Home, combat.Get(id).Height);
        HomeHeight = Motion.Height; _combat.Move(Id, Motion.Position, Motion.Height);
        _area = area; _boss = boss; _abilities = abilities;
        if (boss is not null)
        {
            if (area is null || abilities is null || !abilities.HasActor(id) || area.Kind != AbilityKind.GroundArea ||
                !float.IsFinite((float)area.Range) || (float)area.Range <= 0 ||
                !float.IsFinite((float)area.Radius) || (float)area.Radius <= 0 || area.Range + area.Radius > cellSize * 32 ||
                !float.IsFinite(boss.ImpactSeconds) || boss.ImpactSeconds is <= 0 or > 10)
                throw new ArgumentException("Invalid bounded boss area profile.");
            if (!abilities.TryProfile(id, area.NetworkId, out var profile) ||
                !float.IsFinite((float)profile.CastSeconds) || profile.CastSeconds > 10)
                throw new ArgumentException("Boss area cast must fit the telegraph lifetime budget.");
        }
    }

    private float? _storyAggroRadius;
    public float EffectiveAggroRadius => _storyAggroRadius ?? _options.AggroRadius;
    public void ApplyPatrol(float radius,uint tick)
    {
        if (!float.IsFinite(radius) || radius is <= 0 || radius>_options.AggroRadius) throw new ArgumentException("Patrol cannot increase threat.");
        _storyAggroRadius=radius;
        if (TargetId.IsValid) Return(tick);
    }
    public NetworkEntityId Id { get; private set; }
    internal NpcOptions Options => _options;
    public Vector2 Home { get; private set; }
    public float HomeHeight { get; private set; }
    public NavigationMover Motion { get; }
    public NetworkEntityId TargetId { get; private set; }
    public NpcBehavior Behavior { get; private set; }
    public uint WindupVersion { get; private set; }
    public NpcWindup? Telegraph { get; private set; }
    public NpcArea? Area { get; private set; }
    public uint AreaVersion { get; private set; }
    internal bool UsesCommonAbilities => _abilities is not null;
    internal ActionCapability CurrentCapabilities=>_combat.Get(Id).Action is {Finished:false} action ? action.Definition.Capabilities :
        Behavior!=NpcBehavior.Windup ? ActionCapability.None : _castingArea ? _area!.Action.Capabilities : ActionCapability.Weapon;
    private NetworkEntityId _beforeTaunt;
    private bool _tauntForced;

    internal bool CanDecide=>!_options.DevelopmentPassive && _combat.Get(Id).Health>0 && !Motion.IsDashing && !_combat.IsFleeing(Id) &&
        !_combat.IsActionLocked(Id) && Behavior!=NpcBehavior.Windup;
    internal double DecisionIn=>!CanDecide ? double.PositiveInfinity :
        _waitingForAttack && _combat.Get(Id).ReadyAt>_combat.Time
            ? Math.Min(Math.Max(0,_decisionRemaining),_combat.Get(Id).ReadyAt-_combat.Time) : Math.Max(0,_decisionRemaining);
    internal double ContactEventIn=>_combat.IsActionLocked(Id) || _combat.Get(Id).Health<=0 ? double.PositiveInfinity : Math.Min(
        Behavior==NpcBehavior.Windup && _basicExecution==0 && !_castingArea ? Math.Max(0,_windupRemaining) : double.PositiveInfinity,
        double.PositiveInfinity);
    internal float MovementMultiplier=>Motion.IsDashing ? 1 : _combat.IsFleeing(Id) ? _combat.FearMovement(Id) :
        _combat.IsActionLocked(Id) || Behavior==NpcBehavior.Windup && _basicExecution==0 && !_castingArea ? 0 : _combat.OrdinaryMovement(Id);

    internal void PrepareMotion(uint tick)
    {
        RefreshBasicAction(tick);
        RefreshAbilityAction(tick);
        if(!CanDecide)return;
        var actor=_combat.Get(Id);
        var attackReady=_waitingForAttack && actor.ReadyAt<=_combat.Time;
        if(_decisionRemaining>0 && !attackReady)return;
        _time=_combat.Time;
        _decisionRemaining=_options.DecisionSeconds;Decide(tick);
    }

    public void Move(double delta, uint tick)
    {
        if (!double.IsFinite(delta) || delta <= 0) throw new ArgumentException("Invalid NPC fixed tick delta.");
        _time += delta;
        if (_combat.Get(Id).Health <= 0) { Defeat(tick); return; }
        if(!_combat.IsActionLocked(Id))
        {
            if(Behavior==NpcBehavior.Windup && _basicExecution==0 && !_castingArea)_windupRemaining=Math.Max(0,_windupRemaining-delta);
        }
        if (Motion.IsDashing)
        { Motion.Step((float)delta); _spatial.Move(Id,Motion.Position); _combat.Move(Id,Motion.Position,Motion.Height); return; }
        if(_combat.IsFleeing(Id))
        { Motion.Step((float)(delta*_combat.FearMovement(Id))); _spatial.Move(Id,Motion.Position); _combat.Move(Id,Motion.Position,Motion.Height); return; }
        if (_combat.IsActionLocked(Id)) return;
        if (Behavior == NpcBehavior.Windup && _basicExecution==0 && !_castingArea) return;
        _decisionRemaining -= delta;
        var previous=Motion.Position;
        Motion.Step((float)(delta * _combat.OrdinaryMovement(Id)));
        _spatial.Move(Id, Motion.Position);
        _combat.Move(Id, Motion.Position, Motion.Height);
        _combat.FaceMovement(Id,Motion.Position-previous);
    }

    internal void ResolveContacts(uint tick)
    {
        // Called inside the common contact barrier. A simultaneous player strike
        // cannot revoke an NPC contact that has already passed its validation.
        if (_combat.Get(Id).Health <= 0) { Defeat(tick); return; }
        if (_combat.IsActionLocked(Id)) return;
        RefreshAbilityAction(tick);
        if (_castingArea || _combat.Get(Id).Action is { Finished: false } skillAction && skillAction.DefinitionId == _area?.Id) return;
        if(_basicExecution!=0) { RefreshBasicAction(tick);return; }
        if (Behavior != NpcBehavior.Windup) return;
        if (!ValidTarget(out _))
        {
            TargetId=_combat.SelectNpcTarget(Id,TargetId);
            if(!ValidTarget(out _)){Return(tick);return;}
        }
        if (_windupRemaining > 0)
        {
            Telegraph = Telegraph!.Value with { RemainingSeconds = (float)_windupRemaining, ServerTick = tick };
            return;
        }
        _combat.ExecuteNpcAttack(Id, _sequence, _direction, tick); EndTelegraph(tick);
        Behavior = NpcBehavior.Chasing;
    }

    private void Decide(uint tick)
    {
        _waitingForAttack=false;
        if(Behavior==NpcBehavior.Returning)
        {
            if(Vector2.DistanceSquared(Motion.Position,Home)<.001f){Behavior=NpcBehavior.Idle;_combat.NpcReturnedHome(Id);}
            else{Motion.TrySetTarget(Home,HomeHeight);return;}
        }
        if(Vector2.DistanceSquared(Home,Motion.Position)>_options.LeashRadius*_options.LeashRadius){Return(tick);return;}
        var naturalCurrent=_tauntForced ? _beforeTaunt : TargetId;
        var forced=_combat.Get(Id).Controls.Latest(Content.Shared.Combat.CrowdControlKind.Taunt);
        if(forced is not null && _combat.ValidNpcOpponent(Id,_combat.ResolveSource(forced.Key.Source)))
        {
            if(!_tauntForced) _beforeTaunt=TargetId;
            _tauntForced=true; TargetId=_combat.ResolveSource(forced.Key.Source);
            _combat.EngageNpc(Id,TargetId);
        }
        else
        {
            var engaged=TargetId.IsValid || _combat.Encounter(Id).Threat.Count>0;
            TargetId=_combat.SelectNpcTarget(Id,naturalCurrent);_beforeTaunt=default;_tauntForced=false;
            if(!TargetId.IsValid && engaged){Return(tick);return;}
        }
        if (TargetId.IsValid && !ValidTarget(out _)) { Return(tick); return; }
        if (!TargetId.IsValid)
        {
            _spatial.Query(Motion.Position, EffectiveAggroRadius, _candidates);
            var nearest = float.MaxValue;
            foreach (var id in _candidates)
            {
                if (!_combat.TryGet(id, out var candidate) || !_combat.ValidNpcOpponent(Id,id) ||
                    !_grid.ClearAttack(Motion.Foot, candidate.Foot)) continue;
                var distance = Vector3.DistanceSquared(Motion.Foot, candidate.Foot);
                if (distance > EffectiveAggroRadius * EffectiveAggroRadius) continue;
                if (distance < nearest || (distance == nearest && id.Value < TargetId.Value)) { nearest = distance; TargetId = id; }
            }
            if (!TargetId.IsValid) return;
            _combat.EngageNpc(Id,TargetId);
        }
        var target = _combat.Get(TargetId);
        var actor = _combat.Get(Id);
        var areaAttack = _area is not null && _nextArea &&
            _abilities!.TryProfile(Id, _area.NetworkId, out var areaProfile) && areaProfile.Charges > 0;
        var attackRange = areaAttack ? _area!.Range : actor.Weapon.Range;
        if (Vector3.DistanceSquared(Motion.Foot, target.Foot) <= attackRange * attackRange &&
            _grid.ClearAttack(Motion.Foot, target.Foot))
        {
            Motion.Reset(Motion.Position, Motion.Position, Motion.Height, Motion.Height);
            if(actor.Action is {Finished:false})return;
            if (!areaAttack && actor.ReadyAt > _time) { _waitingForAttack=true;return; }
            var offset = target.Position - Motion.Position;
            _direction = offset.LengthSquared() < 0.000001f ? Vector2.UnitY : Vector2.Normalize(offset);
            _combat.FaceIntent(Id,_direction);
            if (++_sequence == 0) ++_sequence;
            _castingArea = areaAttack;
            _windupRemaining = _options.WindupSeconds;
            Behavior = NpcBehavior.Windup;
            if (areaAttack)
            {
                if (!_abilities!.QueueAiAbility(Id, new(_sequence, tick, _area!.NetworkId, target.Position, AimHeight: target.Height), tick))
                { _castingArea = false; Behavior = NpcBehavior.Chasing; return; }
                _abilitySequence = _sequence;
                _abilityStarted = false;
            }
            else
            {
                if(actor.Weapon.BasicAttack is not null)
                {
                    if(!_combat.ExecuteNpcAttack(Id,_sequence,_direction,tick)) { Behavior=NpcBehavior.Chasing;return; }
                    _basicExecution=actor.Action!.ExecutionId;
                    _windupRemaining=Math.Max(0,actor.Action.PhaseEndsAt-_combat.Time);
                }
                Telegraph = new(Id, _sequence, tick, Motion.Position, _direction, (float)actor.Weapon.Range, (float)_windupRemaining);
                WindupVersion++;
            }
            // Only the boss alternates; player count and client commands never select its next attack.
            if (_area is not null && (areaAttack || !_nextArea)) _nextArea = !areaAttack;
        }
        else
        {
            Behavior = NpcBehavior.Chasing;
            if (!Motion.TrySetTarget(target.Position, target.Height)) Return(tick);
        }
    }

    internal void Interrupt(uint tick, NetworkEntityId target)
    {
        if (target != Id || Behavior != NpcBehavior.Windup) return;
        if(_basicExecution!=0) { RefreshBasicAction(tick);return; }
        if (_castingArea) { RefreshAbilityAction(tick); return; }
        if (_castingArea ? _area?.Interruptible != true : _combat.Get(Id).Kind == CombatEntityKind.Boss) return;
        EndTelegraph(tick); EndArea(tick); _castingArea = false; Behavior = NpcBehavior.Chasing;
    }

    private bool ValidTarget(out Combatant target) => _combat.TryGet(TargetId, out target!) && _combat.ValidNpcOpponent(Id,TargetId) &&
        Vector2.DistanceSquared(Home, Motion.Position) <= _options.LeashRadius * _options.LeashRadius;

    private void Return(uint tick)
    {
        _combat.BeginNpcReturn(Id);_beforeTaunt=default;_tauntForced=false;
        StopBasicAction();
        _abilities?.StopAiAction(Id); _abilitySequence = 0; _abilityStarted = false;
        EndTelegraph(tick); EndArea(tick); _castingArea = false; _waitingForAttack=false; TargetId = default; Behavior = NpcBehavior.Returning;
        Motion.Reset(Motion.Position, Home, Motion.Height, HomeHeight);
    }

    internal void Defeat(uint tick)
    {
        if (Behavior == NpcBehavior.Defeated) return;
        StopBasicAction();
        _abilities?.DisconnectInput(Id); _abilitySequence = 0; _abilityStarted = false;
        EndTelegraph(tick); EndArea(tick); _castingArea = false; _waitingForAttack=false; TargetId = default; Behavior = NpcBehavior.Defeated;
        Motion.Reset(Motion.Position, Motion.Position, Motion.Height, Motion.Height);
    }

    internal void BeginNewLife(NetworkEntityId id)
    {
        // The slot and its world-story modifiers survive, but no combat identity or intent does.
        Id = id;
        TargetId = default;
        Behavior = NpcBehavior.Idle;
        Motion.Reset(Home, Home, HomeHeight, HomeHeight);
        _combat.Move(Id, Motion.Position, Motion.Height);
        _combat.ConfigureNpcEncounter(Id,Home,_options.LeashRadius);
        _decisionRemaining = _windupRemaining = 0;
        _direction = default;
        _sequence = 0;
        _nextArea = _castingArea = false;
        _basicExecution=0;
        _abilitySequence=0;
        _abilityStarted=false;
        _waitingForAttack=false;
        _beforeTaunt=default; _tauntForced=false;
        Telegraph = null;
        Area = null;
        WindupVersion++;
        AreaVersion++;
    }

    internal void ResumeAfterResurrection()
    {
        _combat.ResumeNpcEncounter(Id);
        TargetId=default;Behavior=NpcBehavior.Idle;_decisionRemaining=_windupRemaining=0;_direction=default;
        _castingArea=false;_beforeTaunt=default;_tauntForced=false;
        _basicExecution=0;
        _abilitySequence=0;
        _abilityStarted=false;
        _waitingForAttack=false;
        Motion.Reset(Motion.Position,Motion.Position,Motion.Height,Motion.Height);
    }

    internal NpcSimulation ForkExistingLife(NetworkEntityId id,MovementSettings movement,float cellSize,StatCalculator calculator)
    {
        var actor=new NpcSimulation(id,_combat,_spatial,_grid,movement,_options,cellSize,_area,_boss,calculator,_abilities)
        {Home=Home,HomeHeight=HomeHeight,_storyAggroRadius=_storyAggroRadius,_time=_combat.Time,_sequence=_combat.Get(id).LastSequence};
        return actor;
    }

    private void EndTelegraph(uint tick)
    {
        if (Telegraph is not { RemainingSeconds: > 0 } previous) return;
        Telegraph = previous with { RemainingSeconds = 0, ServerTick = tick }; WindupVersion++;
    }

    private void RefreshBasicAction(uint tick)
    {
        if(_basicExecution==0)return;
        var action=_combat.Get(Id).Action;
        if(action is null || action.Finished || action.ExecutionId!=_basicExecution)
        {
            _basicExecution=0;EndTelegraph(tick);
            if(Behavior==NpcBehavior.Windup)Behavior=NpcBehavior.Chasing;
            _decisionRemaining=0;return;
        }
        if(action.Phase==CombatActionPhase.Preparation)
        {
            Telegraph=Telegraph!.Value with {RemainingSeconds=(float)Math.Max(0,action.PhaseEndsAt-_combat.Time),ServerTick=tick};
            return;
        }
        EndTelegraph(tick);
        Behavior=action.Phase==CombatActionPhase.Active ? NpcBehavior.Windup : NpcBehavior.Chasing;
    }

    private void StopBasicAction()
    {
        if(_basicExecution==0)return;
        _combat.StopBasic(Id,ActionStopReason.Policy,executionId:_basicExecution);_basicExecution=0;
    }

    private void RefreshAbilityAction(uint tick)
    {
        if (_abilitySequence == 0) return;
        if (_abilities!.TryExecution(Id, _abilitySequence, tick, out var execution))
        {
            _abilityStarted = true;
            var phase = execution.Phase == AbilityPhase.Telegraph ? NpcAreaPhase.Telegraph : NpcAreaPhase.Impact;
            if (Area is not { } previous || previous.Sequence != execution.Sequence || previous.Phase != phase)
                AreaVersion++;
            Area = new(Id, execution.Sequence, tick, execution.Position, execution.Radius, (float)execution.RemainingSeconds,
                phase, execution.Height, execution.GroundVolumeHeight);
            _castingArea = execution.Phase is AbilityPhase.Telegraph or AbilityPhase.Charging or AbilityPhase.Channeling;
            var action=_combat.Get(Id).Action;
            if(_basicExecution==0)Behavior=action is { Finished: false } && action.DefinitionId==_area!.Id
                ? NpcBehavior.Windup : NpcBehavior.Chasing;
            return;
        }
        // A queued intent is consumed at the fixed-frame input barrier.
        if (!_abilityStarted && (!_abilities.Results.TryGetValue(Id, out var result) || result.Sequence != _abilitySequence || result.Outcome == AbilityOutcome.Buffered)) return;
        EndArea(tick); _abilitySequence = 0; _castingArea = false;
        if (Behavior == NpcBehavior.Windup) Behavior = NpcBehavior.Chasing;
        _decisionRemaining = 0;
    }

    private void EndArea(uint tick)
    {
        if (Area is not { } previous || previous.Phase == NpcAreaPhase.Finished) return;
        Area = previous with { Phase = NpcAreaPhase.Finished, RemainingSeconds = 0, ServerTick = tick };
        AreaVersion++;
    }
}
