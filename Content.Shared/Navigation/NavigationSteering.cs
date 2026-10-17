using System.Numerics;

namespace Content.Shared.Navigation;

/// <summary>Optional lateral route bias. Failure always falls back to the original traversable segment.</summary>
internal static class NavigationSteering
{
    internal static NavigationMotionSegment Segment(NavigationGrid grid,Vector3 from,Vector3 toward,float speed,
        Vector2 lateral,float horizonDistance,List<Vector3> crossings)
    {
        var straight=NavigationMotionSegment.Toward(from,toward,speed);
        var offset=toward-from;var distance=offset.Length();
        if(lateral==Vector2.Zero||distance<.05f||speed<=0)return straight;
        var heading=offset/distance;
        var horizontal=new Vector2(heading.X,heading.Z);
        if(horizontal.LengthSquared()<.000001f)return straight;
        horizontal=Vector2.Normalize(horizontal);
        lateral-=horizontal*Vector2.Dot(horizontal,lateral);
        if(lateral.LengthSquared()>.25f)lateral=Vector2.Normalize(lateral)*.5f;
        var bias=Math.Min(1,distance/Math.Max(.05f,horizonDistance));
        var direction=Vector3.Normalize(heading+new Vector3(lateral.X,0,lateral.Y)*bias);
        var next=from+direction*Math.Min(distance,horizonDistance);
        if(grid.Surface is {} surface)
        {
            if(!surface.TryLocate(from,.35f,out var start)||!surface.TryLocate(next,.35f,out var end))return straight;
            crossings.Clear();
            if(!surface.Trace(start,end,crossings:crossings))return straight;
            next=end.Position;
            foreach(var crossing in crossings)if(Vector3.DistanceSquared(from,crossing)>.00000001f){next=crossing;break;}
        }
        else if(!grid.TraverseSurface(from,next))return straight;
        return NavigationMotionSegment.Toward(from,next,speed);
    }
}
