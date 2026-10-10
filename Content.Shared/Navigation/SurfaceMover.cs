using System.Numerics;
using Content.Shared.Movement;

namespace Content.Shared.Navigation;

/// <summary>Follows physical polygon portals; the same bounded solver runs in prediction and authority.</summary>
internal sealed class SurfaceMover
{
    private readonly NavigationGrid grid;
    private readonly MovementSettings settings;
    private readonly SurfacePathfinder search;
    private List<Vector3> route = [];
    private List<Vector3> candidate = [];
    private int waypoint;
    public Vector3 Position { get; private set; }
    public Vector3 Target { get; private set; }
    public Vector3 DashDestination { get; private set; }
    public float DashSpeed { get; private set; }
    public bool Moving => DashSpeed > 0 || waypoint < route.Count;

    public SurfaceMover(NavigationGrid grid, MovementSettings settings, Vector3 spawn)
    {
        this.grid = grid; this.settings = settings;
        search = new(grid.Surface!);
        if (!Reset(spawn, spawn)) throw new ArgumentException("Spawn is not on the region surface.");
    }

    public bool SetTarget(Vector3 target)
    {
        if (!MovementSimulation.IsValidTarget(new(target.X, target.Z), settings) ||
            !grid.Surface!.TryLocate(target, .35f, out var goal)) return false;
        // Quantize the intention to its physical surface before comparing: repeated clicks must not restart portal traversal.
        target = goal.Position;
        if (target == Target) return true;
        if (search.Find(Position, target, grid.Surface!.Revision, candidate) != SearchStatus.Success) return false;
        if (DashSpeed > 0) return true;
        Target = candidate[^1]; (route, candidate) = (candidate, route); waypoint = 1;
        return true;
    }

    public bool Reset(Vector3 position, Vector3 target)
    {
        if (!MovementSimulation.IsValidTarget(new(position.X, position.Z), settings) ||
            !grid.Surface!.TryLocate(position, .35f, out var located)) return false;
        var previous = Position;
        Position = located.Position;
        if (search.Find(Position, target, grid.Surface.Revision, candidate) != SearchStatus.Success)
        { Position = previous; return false; }
        Target = candidate[^1]; (route, candidate) = (candidate, route); waypoint = 1; DashSpeed = 0;
        return true;
    }

    public bool Dash(Vector3 destination, float speed)
    {
        if (DashSpeed > 0 || !float.IsFinite(speed) || speed <= 0 || destination == Position ||
            !grid.Surface!.TryLocate(destination, .35f, out var goal) || !grid.TraverseSurface(Position, goal.Position)) return false;
        // Crossings preserve slope boundaries: a dash never cuts through a hill or jumps between floors.
        candidate.Clear();
        if (!grid.Surface.TryLocate(Position, .35f, out var start) || !grid.Surface.Trace(start, goal, crossings: candidate)) return false;
        candidate.Add(goal.Position); (route, candidate) = (candidate, route); waypoint = 0;
        DashDestination = Target = goal.Position; DashSpeed = speed;
        return true;
    }

    public void Step(float delta)
    {
        if (!float.IsFinite(delta) || delta <= 0) return;
        var budget = (DashSpeed > 0 ? DashSpeed : settings.Speed) * delta;
        while (waypoint < route.Count && budget > 0)
        {
            var offset = route[waypoint] - Position; var distance = offset.Length();
            var next = distance <= budget ? route[waypoint] : Position + offset / distance * budget;
            if (!grid.Surface!.TryLocate(next, .35f, out var located) || !grid.TraverseSurface(Position, located.Position))
            { route.Clear(); waypoint = 0; Target = Position; DashSpeed = 0; return; }
            Position = located.Position;
            if (distance > budget) return;
            budget -= distance; waypoint++;
        }
        if (waypoint == route.Count) DashSpeed = 0;
    }
}
