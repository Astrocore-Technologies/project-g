using System.Numerics;

namespace Content.Shared.Navigation;

public static class SurfaceCollision
{
    // Bounded clipping finds the first blocking obstacle using the same broad-phase as attack rays.
    public static float ClearFraction(NavigationGrid grid, Vector3 from, Vector3 to)
    {
        var lo = 0f; var hi = 1f;
        for (var i = 0; i < 14; i++) { var mid = (lo + hi) / 2; if (grid.ClearAttack(from, Vector3.Lerp(from, to, mid))) lo = mid; else hi = mid; }
        return lo;
    }
    public static bool SweptHit(Vector3 from, Vector3 to, Vector3 center, float radius, out float fraction)
    {
        fraction = 0; var offset = from - center; var segment = to - from;
        var c = offset.LengthSquared() - radius * radius; if (c <= 0) return true;
        var a = segment.LengthSquared(); if (a < .000001f) return false;
        var b = Vector3.Dot(offset, segment); var discriminant = b * b - a * c;
        if (discriminant < 0) return false;
        fraction = (-b - MathF.Sqrt(discriminant)) / a; return fraction is >= 0 and <= 1;
    }
}
