using System.Numerics;
using Content.Server.Data;

namespace Content.Server.Combat;

/// <summary>A capsule's medial segment and radius in authoritative world space.</summary>
internal readonly record struct CombatCapsule(Vector3 Center, Vector3 Axis, double HalfSegment, double Radius)
{
    internal static CombatCapsule Create(HurtboxDefinition profile, Vector3 foot, Vector2 facing, bool prone)
    {
        var center = foot + Vector3.UnitY * (prone ? profile.Radius : profile.Height * .5f);
        var axis = prone ? new Vector3(facing.X, 0, facing.Y) : Vector3.UnitY;
        return new(center, axis, profile.Height * .5d - profile.Radius, profile.Radius);
    }

    internal double DistanceSquared(double x, double y, double z)
    {
        var length = Math.Sqrt((double)Axis.X * Axis.X + (double)Axis.Y * Axis.Y + (double)Axis.Z * Axis.Z);
        var ax = Axis.X / length; var ay = Axis.Y / length; var az = Axis.Z / length;
        var along = Math.Clamp(x * ax + y * ay + z * az, -HalfSegment, HalfSegment);
        x -= ax * along; y -= ay * along; z -= az * along;
        return x * x + y * y + z * z;
    }

    internal bool IntersectsSphere(Vector3 center, double radius)
        => DistanceSquared((double)center.X - Center.X, (double)center.Y - Center.Y, (double)center.Z - Center.Z)
            <= (Radius + radius) * (Radius + radius);
}
