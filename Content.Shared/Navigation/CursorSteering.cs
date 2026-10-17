using System.Numerics;

namespace Content.Shared.Navigation;

/// <summary>Direct cursor steering clips at the first obstruction; it never invokes pathfinding or slides around it.</summary>
internal static class CursorSteering
{
    private const float Horizon = .25f;
    private const float CollisionSkin = .001f;

    internal static NavigationMotionSegment Segment(NavigationGrid grid, Vector3 from, Vector2 target,
        float speed, float stopDistance, List<Vector3> crossings)
    {
        var offset = target - new Vector2(from.X, from.Z);
        var distance = offset.Length();
        if (speed <= 0 || distance <= stopDistance) return NavigationMotionSegment.Stationary;
        var toward = from + new Vector3(offset.X, 0, offset.Y) * (Math.Min(distance, Horizon) / distance);
        var end = from;
        bool Clear(float fraction, out Vector3 point)
        {
            point = Vector3.Lerp(from, toward, fraction);
            if (grid.Surface is not { } mesh) return grid.CanTraverse(new(from.X, from.Z), new(point.X, point.Z));
            if (!mesh.TryLocate(from, .35f, out var start) || !mesh.TryLocate(point, .35f, out var goal) || !mesh.Trace(start, goal)) return false;
            point = goal.Position; return true;
        }
        if (!Clear(1, out end))
        {
            var low = 0f; var high = 1f;
            end = from;
            for (var i = 0; i < 14; i++)
            {
                var middle = (low + high) / 2;
                if (Clear(middle, out var valid)) { low = middle; end = valid; } else high = middle;
            }
            // Stay outside the surface locator's edge tolerance instead of creeping into it each tick.
            var safe = Math.Max(0, low - CollisionSkin / Vector3.Distance(from, toward));
            if (!Clear(safe, out end)) end = from;
        }
        if (grid.Surface is { } surface && surface.TryLocate(from, .35f, out var a) && surface.TryLocate(end, .35f, out var b))
        {
            crossings.Clear();
            if (!surface.Trace(a, b, crossings: crossings)) return NavigationMotionSegment.Stationary;
            foreach (var crossing in crossings)
                if (Vector3.DistanceSquared(from, crossing) > .00000001f) { end = crossing; break; }
        }
        return Vector3.DistanceSquared(from, end) <= .00000001f
            ? NavigationMotionSegment.Stationary : NavigationMotionSegment.Toward(from, end, speed);
    }
}
