using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
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

    public NpcSimulation(NetworkEntityId id, CombatSimulation combat, SpatialIndex spatial, NavigationGrid grid,
        MovementSettings movement, NpcOptions options, float cellSize)
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
    }

    public NetworkEntityId Id { get; }
    public Vector2 Home { get; }
    public NavigationMover Motion { get; }
    public NetworkEntityId TargetId { get; private set; }
    public NpcBehavior Behavior { get; private set; }
    public uint WindupVersion { get; private set; }
    public NpcWindup? Telegraph { get; private set; }

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
        if (Behavior != NpcBehavior.Windup) return;
        if (!ValidTarget(out _)) { Return(tick); return; }
        _windupRemaining -= delta;
        if (_windupRemaining > 0)
        {
            Telegraph = Telegraph!.Value with { RemainingSeconds = _windupRemaining, ServerTick = tick };
            return;
        }
        _combat.ExecuteNpcAttack(Id, _sequence, _direction, tick);
        EndTelegraph(tick);
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
            _spatial.Query(Motion.Position, _options.AggroRadius, _candidates);
            var nearest = float.MaxValue;
            foreach (var id in _candidates)
            {
                if (!_combat.TryGet(id, out var candidate) || candidate.Kind != CombatEntityKind.Player || candidate.Health <= 0 ||
                    !_grid.CanTraverse(Motion.Position, candidate.Position)) continue;
                var distance = Vector2.DistanceSquared(Motion.Position, candidate.Position);
                if (distance < nearest || (distance == nearest && id.Value < TargetId.Value)) { nearest = distance; TargetId = id; }
            }
            if (!TargetId.IsValid) return;
        }
        var target = _combat.Get(TargetId);
        var actor = _combat.Get(Id);
        if (Vector2.DistanceSquared(Motion.Position, target.Position) <= actor.Weapon.Range * actor.Weapon.Range &&
            _grid.CanTraverse(Motion.Position, target.Position))
        {
            Motion.Reset(Motion.Position, Motion.Position);
            if (actor.ReadyAt > 0 && actor.ReadyAt > _time) return;
            var offset = target.Position - Motion.Position;
            _direction = offset.LengthSquared() < 0.000001f ? Vector2.UnitY : Vector2.Normalize(offset);
            if (++_sequence == 0) ++_sequence;
            _windupRemaining = _options.WindupSeconds;
            Behavior = NpcBehavior.Windup;
            Telegraph = new(Id, _sequence, tick, Motion.Position, _direction, (float)actor.Weapon.Range, _windupRemaining);
            WindupVersion++;
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
        EndTelegraph(tick); TargetId = default; Behavior = NpcBehavior.Returning;
        Motion.Reset(Motion.Position, Home);
    }

    private void Defeat(uint tick)
    {
        if (Behavior == NpcBehavior.Defeated) return;
        EndTelegraph(tick); TargetId = default; Behavior = NpcBehavior.Defeated;
        Motion.Reset(Motion.Position, Motion.Position);
    }

    private void EndTelegraph(uint tick)
    {
        if (Telegraph is not { RemainingSeconds: > 0 } previous) return;
        Telegraph = previous with { RemainingSeconds = 0, ServerTick = tick }; WindupVersion++;
    }
}
