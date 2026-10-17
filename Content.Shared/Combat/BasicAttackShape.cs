using System.Numerics;

namespace Content.Shared.Combat;

/// <summary>Public aim validation and legacy flat footprints; authoritative volumes and LOS belong to the server.</summary>
public static class BasicAttackShape
{
    public static bool IsValidDirection(Vector2 direction) =>
        float.IsFinite(direction.X) && float.IsFinite(direction.Y) &&
        direction.LengthSquared() is >= 0.99f and <= 1.01f;

    public static bool IsValidDirection3D(Vector2 direction,float directionY)=>
        float.IsFinite(direction.X) && float.IsFinite(direction.Y) && float.IsFinite(directionY) &&
        direction.LengthSquared()+directionY*directionY is >=.99f and <=1.01f;

    public static Vector3 NormalizeDirection3D(Vector2 direction,float directionY)
        =>Vector3.Normalize(new(direction.X,directionY,direction.Y));

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
