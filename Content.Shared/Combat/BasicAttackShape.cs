using System.Numerics;

namespace Content.Shared.Combat;

/// <summary>Public flat-arena cone geometry; authoritative position and LOS belong to the server.</summary>
public static class BasicAttackShape
{
    public static bool IsValidDirection(Vector2 direction) =>
        float.IsFinite(direction.X) && float.IsFinite(direction.Y) &&
        direction.LengthSquared() is >= 0.99f and <= 1.01f;

    public static bool Contains(Vector2 origin, Vector2 direction, Vector2 target, float range, float halfAngle)
    {
        var offset = target - origin;
        var distanceSquared = offset.LengthSquared();
        if (distanceSquared > range * range)
            return false;
        return distanceSquared < 0.000001f ||
            Vector2.Dot(direction, offset / MathF.Sqrt(distanceSquared)) >= MathF.Cos(halfAngle);
    }
}
