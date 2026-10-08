using System.Numerics;

namespace Content.Shared.Combat;

/// <summary>First segment-circle contact; checks the whole segment to avoid projectile tunneling.</summary>
public static class SweptCircle
{
    public static bool TryHit(Vector2 from, Vector2 to, Vector2 center, float radius, out float fraction)
    {
        fraction = 0;
        var offset = from - center;
        var c = offset.LengthSquared() - radius * radius;
        if (c <= 0) return true;
        var segment = to - from;
        var a = segment.LengthSquared();
        if (a < 0.000001f) return false;
        var b = Vector2.Dot(offset, segment);
        var discriminant = b * b - a * c;
        if (discriminant < 0) return false;
        fraction = (-b - MathF.Sqrt(discriminant)) / a;
        return fraction is >= 0 and <= 1;
    }
}
