using System.Numerics;

namespace Content.Shared.Navigation;

public static class SurfaceDash
{
    public static bool TryDestination(NavigationGrid grid, Vector3 start, Vector2 direction, float range,
        out Vector2 destination, out float height)
    {
        destination = new(start.X, start.Z); height = start.Y;
        if (grid.Surface is null) return DashGeometry.TryDestination(grid, destination, direction, range, out destination);
        if (!float.IsFinite(range) || range <= 0 || !float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || direction.LengthSquared() < .000001f ||
            !grid.Surface.TryLocate(start, .35f, out var origin)) return false;
        direction = Vector2.Normalize(direction);
        var column = new List<SurfaceLocation>(); var crossings = new List<Vector3>();
        var lo = 0f; var hi = range;
        for (var i = 0; i < 16; i++)
        {
            var distance = (lo + hi) / 2; var point = new Vector2(start.X, start.Z) + direction * distance;
            grid.Surface.SampleColumn(point.X, point.Y, column); var found = false;
            foreach (var target in column)
            {
                crossings.Clear();
                if (!grid.Surface.Trace(origin, target, crossings: crossings)) continue;
                var length = 0f; var previous = origin.Position;
                foreach (var p in crossings) { length += Vector3.Distance(previous, p); previous = p; }
                length += Vector3.Distance(previous, target.Position);
                if (length > range) continue;
                found = true; destination = point; height = target.Position.Y; break;
            }
            if (found) lo = distance; else hi = distance;
        }
        return lo > .001f;
    }
}
