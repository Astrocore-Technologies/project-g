using System.Numerics;

namespace Content.Shared.Navigation;

/// <summary>A read-only straight portion of an existing route, ending at the next waypoint.</summary>
public readonly record struct NavigationMotionSegment(Vector3 Velocity,double Seconds)
{
    public static NavigationMotionSegment Stationary=>new(Vector3.Zero,double.PositiveInfinity);
    internal static NavigationMotionSegment Toward(Vector3 from,Vector3 to,float speed)
    {
        var offset=to-from;var distance=offset.Length();
        return distance>0 && speed>0 ? new(offset/distance*speed,distance/(double)speed) : Stationary;
    }
}
