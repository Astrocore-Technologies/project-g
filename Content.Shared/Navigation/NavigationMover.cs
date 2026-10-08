using System.Numerics;
using Content.Shared.Movement;

namespace Content.Shared.Navigation;

/// <summary>Shared route following; the server owns its authoritative instance.</summary>
public sealed class NavigationMover
{
    private readonly NavigationGrid _grid;
    private readonly MovementSettings _settings;
    private readonly NavigationPathfinder _pathfinder;
    private List<Vector2> _route = new();
    private List<Vector2> _candidate = new();
    private int _waypoint;
    public Vector2 Position { get; private set; }
    public Vector2 Target { get; private set; }
    public bool IsMoving => IsDashing || _waypoint < _route.Count;
    public bool IsDashing => DashSpeed > 0;
    public Vector2 DashDestination { get; private set; }
    public float DashSpeed { get; private set; }

    public NavigationMover(NavigationGrid grid, MovementSettings settings,
        NavigationPathfinder pathfinder, Vector2 spawn)
    {
        if (!grid.IsWalkable(spawn))
            throw new ArgumentException("Spawn is not walkable.", nameof(spawn));
        _grid = grid;
        _settings = settings;
        _pathfinder = pathfinder;
        Position = Target = spawn;
    }

    public bool TrySetTarget(Vector2 target)
    {
        if (IsDashing)
            return MovementSimulation.IsValidTarget(target, _settings) && _grid.IsWalkable(target);
        if (target == Target)
            return true;
        if (!MovementSimulation.IsValidTarget(target, _settings) ||
            !_pathfinder.TryFindPath(Position, target, _candidate))
            return false;
        SetRoute(target);
        return true;
    }

    public bool Reset(Vector2 position, Vector2 target)
    {
        if (!MovementSimulation.IsValidTarget(target, _settings) ||
            !_pathfinder.TryFindPath(position, target, _candidate))
            return false;
        Position = position;
        DashSpeed = 0;
        SetRoute(target);
        return true;
    }

    private void SetRoute(Vector2 target)
    {
        Target = target;
        (_route, _candidate) = (_candidate, _route);
        _waypoint = _route.Count == 1 && _route[0] == Position ? 1 : 0;
    }

    public void Step(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
            return;
        if (IsDashing)
        {
            var offset = DashDestination - Position;
            var distance = offset.Length();
            var next = distance <= DashSpeed * deltaSeconds ? DashDestination
                : Position + offset / distance * DashSpeed * deltaSeconds;
            if (!_grid.CanTraverse(Position, next))
            {
                DashSpeed = 0;
                Reset(Position, Position);
                return;
            }
            Position = next;
            if (Position == DashDestination)
            {
                DashSpeed = 0;
                if (!Reset(Position, Target)) Reset(Position, Position);
            }
            return; // No extra ordinary movement budget on the final dash tick.
        }
        var budget = _settings.Speed * deltaSeconds;
        while (IsMoving && budget > 0f)
        {
            var offset = _route[_waypoint] - Position;
            var distance = offset.Length();
            var next = distance <= budget ? _route[_waypoint] : Position + offset / distance * budget;
            if (!_grid.CanTraverse(Position, next))
            {
                // Defensive stop: route data never authorizes crossing solid geometry.
                _route.Clear();
                Target = Position;
                return;
            }
            Position = next;
            if (distance > budget)
                return;
            budget -= distance;
            _waypoint++;
        }
    }

    public bool TryStartDash(Vector2 destination, float speed)
    {
        if (IsDashing || !float.IsFinite(speed) || speed <= 0 || destination == Position ||
            !_grid.CanTraverse(Position, destination))
            return false;
        DashDestination = destination;
        DashSpeed = speed;
        Target = destination;
        _route.Clear();
        _waypoint = 0;
        return true;
    }

    public bool Restore(Vector2 position, Vector2 target, Vector2 dashDestination, float dashSpeed) =>
        Reset(position, target) && (dashSpeed == 0 || TryStartDash(dashDestination, dashSpeed));
}
