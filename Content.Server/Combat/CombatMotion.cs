using System.Numerics;

namespace Content.Server.Combat;

/// <summary>Authoritative motion until the next route/control boundary. Airborne height is quadratic.</summary>
internal readonly record struct CombatMotion(Vector3 Velocity,Vector3 Acceleration)
{
    internal Vector3 Offset(double seconds)=>Velocity*(float)seconds+Acceleration*(float)(seconds*seconds*.5);
}
