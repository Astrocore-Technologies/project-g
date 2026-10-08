using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
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
    private float _decisionRemaining;
    private float _windupRemaining;
    private Vector2 _direction;
    private uint _sequence;
    private double _time;
    private readonly AbilityDefinition? _area;
    private readonly BossOptions? _boss;
    private readonly float _areaCastSeconds;
    private bool _nextArea;
    private bool _castingArea;

    public NpcSimulation(NetworkEntityId id, CombatSimulation combat, SpatialIndex spatial, NavigationGrid grid,
        MovementSettings movement, NpcOptions options, float cellSize, AbilityDefinition? area = null,
        BossOptions? boss = null, StatCalculator? calculator = null)
    {
        if (!float.IsFinite(options.Speed) || options.Speed is <= 0 or > 30 ||
            !float.IsFinite(options.AggroRadius) || options.AggroRadius <= 0 ||
            !float.IsFinite(options.LeashRadius) || options.LeashRadius < options.AggroRadius || options.LeashRadius > cellSize * 32 ||
            !float.IsFinite(options.DecisionSeconds) || options.DecisionSeconds is < 0.1f or > 2 ||
            !float.IsFinite(options.WindupSeconds) || options.WindupSeconds is < 0.2f or > 10)
            throw new ArgumentException("Invalid bounded NPC settings.");
        Id = id; _combat = combat; _spatial = spatial; _grid = grid; _options = options;
        Home = combat.Get(id).Position;
        Motion = new NavigationMover(grid, movement with { Speed = options.Speed }, new NavigationPathfinder(grid), Home);
        _area = area; _boss = boss;
        if (boss is not null)
        {
            if (area is null || calculator is null || area.Kind != AbilityKind.GroundArea || area.ManaCost != 0 ||
                !float.IsFinite((float)area.Range) || (float)area.Range <= 0 ||
                !float.IsFinite((float)area.Radius) || (float)area.Radius <= 0 || area.Range + area.Radius > cellSize * 32 ||
                !float.IsFinite(boss.ImpactSeconds) || boss.ImpactSeconds is <= 0 or > 10)
                throw new ArgumentException("Invalid bounded boss area profile.");
            _areaCastSeconds = (float)calculator.CastDuration(area.CastSeconds, combat.Get(id).Stats);
            if (!float.IsFinite(_areaCastSeconds) || _areaCastSeconds is < 0.2f or > 10)
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
    public NetworkEntityId Id { get; }
    public Vector2 Home { get; }
    public NavigationMover Motion { get; }
    public NetworkEntityId TargetId { get; private set; }
    public NpcBehavior Behavior { get; private set; }
    public uint WindupVersion { get; private set; }
    public NpcWindup? Telegraph { get; private set; }
    public NpcArea? Area { get; private set; }
    public uint AreaVersion { get; private set; }

    public void Move(float delta, uint tick)
    {
        if (!float.IsFinite(delta) || delta <= 0) throw new ArgumentException("Invalid NPC fixed tick delta.");
        _time += delta;
        if (_combat.Get(Id).Health <= 0) { Defeat(tick); return; }
        if (Behavior == NpcBehavior.Windup) return;
        _decisionRemaining -= delta;
        if (_decisionRemaining <= 0)
        {
            _decisionRemaining = _options.DecisionSeconds;
            Decide(tick);
        }
        Motion.Step(delta);
        _spatial.Move(Id, Motion.Position);
        _combat.Move(Id, Motion.Position);
    }

    public void Resolve(float delta, uint tick)
    {
        // Player attacks/casts resolve first: killing a winding-up NPC cancels its strike.
        if (_combat.Get(Id).Health <= 0) { Defeat(tick); return; }
        if (Area is { Phase: NpcAreaPhase.Impact } impact)
        {
            var remaining = Math.Max(0, impact.RemainingSeconds - delta);
            Area = impact with { RemainingSeconds = remaining, ServerTick = tick };
            if (remaining == 0) EndArea(tick);
        }
        if (Behavior != NpcBehavior.Windup) return;
        if (!ValidTarget(out _)) { Return(tick); return; }
        _windupRemaining -= delta;
        if (_windupRemaining > 0)
        {
            if (_castingArea) Area = Area!.Value with { RemainingSeconds = _windupRemaining, ServerTick = tick };
            else Telegraph = Telegraph!.Value with { RemainingSeconds = _windupRemaining, ServerTick = tick };
            return;
        }
        if (_castingArea)
        {
            var zone = Area!.Value;
            if (_combat.ExecuteNpcArea(Id, _sequence, zone.Center, _area!, tick))
            {
                Area = zone with { Phase = NpcAreaPhase.Impact, RemainingSeconds = _boss!.ImpactSeconds, ServerTick = tick };
                AreaVersion++;
            }
            else EndArea(tick);
        }
        else { _combat.ExecuteNpcAttack(Id, _sequence, _direction, tick); EndTelegraph(tick); }
        _castingArea = false;
        Behavior = NpcBehavior.Chasing;
    }

    private void Decide(uint tick)
    {
        if (Behavior == NpcBehavior.Returning)
        {
            if (Vector2.DistanceSquared(Motion.Position, Home) < 0.001f) Behavior = NpcBehavior.Idle;
            else { Motion.TrySetTarget(Home); return; }
        }
        if (TargetId.IsValid && !ValidTarget(out _)) { Return(tick); return; }
        if (!TargetId.IsValid)
        {
            _spatial.Query(Motion.Position, EffectiveAggroRadius, _candidates);
            var nearest = float.MaxValue;
            foreach (var id in _candidates)
            {
                if (!_combat.TryGet(id, out var candidate) || candidate.Kind != CombatEntityKind.Player || candidate.Health <= 0 ||
                    !_grid.CanTraverse(Motion.Position, candidate.Position)) continue;
                var distance = Vector2.DistanceSquared(Motion.Position, candidate.Position);
                if (distance > EffectiveAggroRadius * EffectiveAggroRadius) continue;
                if (distance < nearest || (distance == nearest && id.Value < TargetId.Value)) { nearest = distance; TargetId = id; }
            }
            if (!TargetId.IsValid) return;
        }
        var target = _combat.Get(TargetId);
        var actor = _combat.Get(Id);
        var areaAttack = _area is not null && _nextArea;
        var attackRange = areaAttack ? _area!.Range : actor.Weapon.Range;
        if (Vector2.DistanceSquared(Motion.Position, target.Position) <= attackRange * attackRange &&
            _grid.CanTraverse(Motion.Position, target.Position))
        {
            Motion.Reset(Motion.Position, Motion.Position);
            if (actor.ReadyAt > 0 && actor.ReadyAt > _time) return;
            var offset = target.Position - Motion.Position;
            _direction = offset.LengthSquared() < 0.000001f ? Vector2.UnitY : Vector2.Normalize(offset);
            if (++_sequence == 0) ++_sequence;
            _castingArea = areaAttack;
            _windupRemaining = areaAttack ? _areaCastSeconds : _options.WindupSeconds;
            Behavior = NpcBehavior.Windup;
            if (areaAttack)
            {
                Area = new(Id, _sequence, tick, target.Position, (float)_area!.Radius, _windupRemaining, NpcAreaPhase.Telegraph);
                AreaVersion++;
            }
            else
            {
                Telegraph = new(Id, _sequence, tick, Motion.Position, _direction, (float)actor.Weapon.Range, _windupRemaining);
                WindupVersion++;
            }
            // Only the boss alternates; player count and client commands never select its next attack.
            if (_area is not null) _nextArea = !areaAttack;
        }
        else
        {
            Behavior = NpcBehavior.Chasing;
            if (!Motion.TrySetTarget(target.Position)) Return(tick);
        }
    }

    private bool ValidTarget(out Combatant target) => _combat.TryGet(TargetId, out target!) && target.Health > 0 &&
        target.Kind == CombatEntityKind.Player && Vector2.DistanceSquared(Home, target.Position) <= _options.LeashRadius * _options.LeashRadius &&
        Vector2.DistanceSquared(Home, Motion.Position) <= _options.LeashRadius * _options.LeashRadius;

    private void Return(uint tick)
    {
        EndTelegraph(tick); EndArea(tick); _castingArea = false; TargetId = default; Behavior = NpcBehavior.Returning;
        Motion.Reset(Motion.Position, Home);
    }

    private void Defeat(uint tick)
    {
        if (Behavior == NpcBehavior.Defeated) return;
        EndTelegraph(tick); EndArea(tick); _castingArea = false; TargetId = default; Behavior = NpcBehavior.Defeated;
        Motion.Reset(Motion.Position, Motion.Position);
    }

    private void EndTelegraph(uint tick)
    {
        if (Telegraph is not { RemainingSeconds: > 0 } previous) return;
        Telegraph = previous with { RemainingSeconds = 0, ServerTick = tick }; WindupVersion++;
    }

    private void EndArea(uint tick)
    {
        if (Area is not { } previous || previous.Phase == NpcAreaPhase.Finished) return;
        Area = previous with { Phase = NpcAreaPhase.Finished, RemainingSeconds = 0, ServerTick = tick };
        AreaVersion++;
    }
}
