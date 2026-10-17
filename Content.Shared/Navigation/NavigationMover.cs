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
    private Vector2 _avoidance;
    private readonly List<Vector3> _avoidanceCrossings=new();
    private float _avoidanceHorizon=.25f;
    private Vector2? _cursorTarget;
    private Vector2 _target;
    public bool IsCursor => _cursorTarget.HasValue;
    public void SetAvoidance(Vector2 lateral,float horizonDistance=.25f)
    {
        if(!float.IsFinite(lateral.X)||!float.IsFinite(lateral.Y)||!float.IsFinite(horizonDistance)||horizonDistance<=0)
            throw new ArgumentException("Invalid navigation steering.");
        _avoidance=lateral;_avoidanceHorizon=horizonDistance;
        if(_surface is not null){_surface.Avoidance=lateral;_surface.AvoidanceHorizon=horizonDistance;}
    }
    public float Height => _surface?.Position.Y ?? 0;
    public float TargetHeight => IsCursor ? Height : _surface?.Target.Y ?? 0;
    public float DashHeight => _surface?.DashDestination.Y ?? 0;
    public Vector3 Foot => new(Position.X, Height, Position.Y);
    public Vector2 Position { get; private set; }
    public Vector2 Target { get => _cursorTarget ?? _target; private set => _target = value; }
    public bool IsMoving => IsCursor ? Vector2.DistanceSquared(Position, Target) > _settings.StopDistance * _settings.StopDistance
        : _surface?.Moving ?? (IsDashing || _waypoint < _route.Count);
    public bool IsDashing => (_surface?.DashSpeed ?? DashSpeed) > 0;
    public Vector2 DashDestination { get; private set; }
    public float DashSpeed { get; private set; }
    public float WalkSpeed=>_settings.Speed;

    public NavigationMotionSegment CurrentSegment(float movementMultiplier=1)
    {
        if(!float.IsFinite(movementMultiplier) || movementMultiplier<0)throw new ArgumentOutOfRangeException(nameof(movementMultiplier));
        if (IsCursor) return CursorSteering.Segment(_grid, Foot, Target, _settings.Speed * movementMultiplier, _settings.StopDistance, _avoidanceCrossings);
        if(_surface is not null)return _surface.CurrentSegment(movementMultiplier);
        if(IsDashing)return NavigationMotionSegment.Toward(Foot,new(DashDestination.X,0,DashDestination.Y),DashSpeed);
        for(var i=_waypoint;i<_route.Count;i++)
            if(_route[i]!=Position)
                return NavigationSteering.Segment(_grid,Foot,new(_route[i].X,0,_route[i].Y),_settings.Speed*movementMultiplier,
                    _avoidance,_avoidanceHorizon,_avoidanceCrossings);
        return NavigationMotionSegment.Stationary;
    }

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
        var result = _surface.SetTarget(new(target.X, height, target.Y));
        if (result) _cursorTarget = null;
        SyncSurface(); return result;
    }

    public bool TrySetCursor(Vector2 target)
    {
        if (IsDashing || !MovementSimulation.IsValidTarget(target, _settings)) return false;
        if (!IsCursor && !Reset(Position, Position, Height, Height)) return false;
        _cursorTarget = target;
        return true;
    }

    public bool Reset(Vector2 position, Vector2 target, float height, float targetHeight)
    {
        if (_surface is null) return height == 0 && targetHeight == 0 && Reset(position, target);
        var result = _surface.Reset(new(position.X, height, position.Y), new(target.X, targetHeight, target.Y));
        if (result) _cursorTarget = null;
        SyncSurface(); return result;
    }

    public bool TrySetTarget(Vector2 target)
    {
        if (_surface is not null) return TrySetTarget(target, Height);
        if (IsDashing)
            return MovementSimulation.IsValidTarget(target, _settings) && _grid.IsWalkable(target);
        if (!IsCursor && target == Target)
            return true;
        if (!MovementSimulation.IsValidTarget(target, _settings) ||
            !_pathfinder.TryFindPath(Position, target, _candidate))
            return false;
        SetRoute(target);
        _cursorTarget = null;
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
        _cursorTarget = null;
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
        if (IsCursor)
        {
            if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
            for (var step = 0; step < 32 && deltaSeconds > 0; step++)
            {
                var segment = CurrentSegment();
                if (segment.Velocity == Vector3.Zero) return;
                var seconds = Math.Min(deltaSeconds, segment.Seconds);
                var next = Foot + segment.Velocity * (float)seconds;
                if (_surface is not null) _surface.PlaceDirect(next);
                Position = new(next.X, next.Z); SyncSurface();
                deltaSeconds -= (float)seconds;
            }
            return;
        }
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
            if(distance<.0001f){Position=_route[_waypoint];_waypoint++;continue;}
            var segment=CurrentSegment();
            var travelled=(float)Math.Min(budget,_settings.Speed*segment.Seconds);
            var next=Position+new Vector2(segment.Velocity.X,segment.Velocity.Z)* (travelled/_settings.Speed);
            if(_avoidance==Vector2.Zero && distance<=budget)next=_route[_waypoint];
            if (!_grid.CanTraverse(Position, next))
            {
                // Defensive stop: route data never authorizes crossing solid geometry.
                _route.Clear();
                Target = Position;
                return;
            }
            Position = next;
            if (_avoidance!=Vector2.Zero)
            {
                budget-=travelled;
                if(Vector2.DistanceSquared(Position,_route[_waypoint])<.00000001f)_waypoint++;
                if(travelled<=0)return;
                continue;
            }
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
        _cursorTarget = null;
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
        var result = _surface.Dash(new(destination.X, height, destination.Y), speed);
        if (result) _cursorTarget = null;
        SyncSurface(); return result;
    }

    public bool Restore(Vector2 position, Vector2 target, Vector2 dashDestination, float dashSpeed,
        float height, float targetHeight, float dashHeight, bool directCursor = false) =>
        (directCursor ? Reset(position, position, height, height) && TrySetCursor(target) : Reset(position, target, height, targetHeight)) &&
        (dashSpeed == 0 || TryStartDash(dashDestination, dashSpeed, dashHeight));
}
