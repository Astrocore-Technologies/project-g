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
    private readonly SurfaceMover? _surface;
    public float Height => _surface?.Position.Y ?? 0;
    public float TargetHeight => _surface?.Target.Y ?? 0;
    public float DashHeight => _surface?.DashDestination.Y ?? 0;
    public Vector3 Foot => new(Position.X, Height, Position.Y);
    public Vector2 Position { get; private set; }
    public Vector2 Target { get; private set; }
    public bool IsMoving => _surface?.Moving ?? (IsDashing || _waypoint < _route.Count);
    public bool IsDashing => (_surface?.DashSpeed ?? DashSpeed) > 0;
    public Vector2 DashDestination { get; private set; }
    public float DashSpeed { get; private set; }

    public NavigationMover(NavigationGrid grid, MovementSettings settings,
        NavigationPathfinder pathfinder, Vector2 spawn, float height = 0)
    {
        if (grid.Surface is not null) _surface = new(grid, settings, new(spawn.X, height, spawn.Y));
        else if (!grid.IsWalkable(spawn))
            throw new ArgumentException("Spawn is not walkable.", nameof(spawn));
        _grid = grid;
        _settings = settings;
        _pathfinder = pathfinder;
        Position = Target = spawn;
        SyncSurface();
    }

    private void SyncSurface()
    {
        if (_surface is null) return;
        Position = new(_surface.Position.X, _surface.Position.Z); Target = new(_surface.Target.X, _surface.Target.Z);
        DashDestination = new(_surface.DashDestination.X, _surface.DashDestination.Z); DashSpeed = _surface.DashSpeed;
    }

    public bool TrySetTarget(Vector2 target, float height)
    {
        if (_surface is null) return height == 0 && TrySetTarget(target);
        var result = _surface.SetTarget(new(target.X, height, target.Y)); SyncSurface(); return result;
    }

    public bool Reset(Vector2 position, Vector2 target, float height, float targetHeight)
    {
        if (_surface is null) return height == 0 && targetHeight == 0 && Reset(position, target);
        var result = _surface.Reset(new(position.X, height, position.Y), new(target.X, targetHeight, target.Y)); SyncSurface(); return result;
    }

    public bool TrySetTarget(Vector2 target)
    {
        if (_surface is not null) return TrySetTarget(target, Height);
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
        if (_surface is not null) return Reset(position, target, Height, Height);
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
        if (_surface is not null) { _surface.Step(deltaSeconds); SyncSurface(); return; }
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
        if (_surface is not null) return TryStartDash(destination, speed, Height);
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

    public bool TryStartDash(Vector2 destination, float speed, float height)
    {
        if (_surface is null) return height == 0 && TryStartDash(destination, speed);
        var result = _surface.Dash(new(destination.X, height, destination.Y), speed); SyncSurface(); return result;
    }

    public bool Restore(Vector2 position, Vector2 target, Vector2 dashDestination, float dashSpeed,
        float height, float targetHeight, float dashHeight) => Reset(position, target, height, targetHeight) &&
        (dashSpeed == 0 || TryStartDash(dashDestination, dashSpeed, dashHeight));
}
