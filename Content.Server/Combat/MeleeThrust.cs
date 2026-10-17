using System.Numerics;

namespace Content.Server.Combat;

/// <summary>A spherical blade tip advances along a straight medial segment, with its outside ending at Range.</summary>
internal readonly struct MeleeThrust(Vector3 origin,Vector3 direction,double range,double radius)
{
    internal bool TryFirstContact(CombatCapsule body,out double progress,out Vector3 bladePoint)
    {
        if(!double.IsFinite(range) || !double.IsFinite(radius) || radius<=0 || range<radius ||
            !float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z) || direction==Vector3.Zero)
            throw new ArgumentException("Invalid thrust geometry.");
        var path=Vector3.Normalize(direction)*(float)(range-radius);var offset=origin-body.Center;
        var hit=ProjectileCollision.TryCapsuleTime(offset.X,offset.Y,offset.Z,path,Vector3.Zero,body,radius,1,out progress);
        bladePoint=origin+path*(float)progress;return hit;
    }
}
