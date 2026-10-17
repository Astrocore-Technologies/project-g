using System.Numerics;

namespace Content.Server.Combat;

/// <summary>A closed vertical volume above its authored ground anchor, tested against the entire stable body.</summary>
internal readonly struct GroundCylinder(Vector3 bottom,double radius,double height)
{
    internal bool TryContact(CombatCapsule body,out Vector3 witness)
    {
        if(!float.IsFinite(bottom.X) || !float.IsFinite(bottom.Y) || !float.IsFinite(bottom.Z) ||
            !double.IsFinite(radius) || !double.IsFinite(height) || radius<=0 || height<=0)
            throw new ArgumentException("Invalid ground cylinder geometry.");
        var length=Math.Sqrt((double)body.Axis.X*body.Axis.X+(double)body.Axis.Y*body.Axis.Y+(double)body.Axis.Z*body.Axis.Z);
        if(length==0 || !double.IsFinite(length))throw new ArgumentException("Invalid body axis.");
        var ax=body.Axis.X/length;var ay=body.Axis.Y/length;var az=body.Axis.Z/length;
        var cx=(double)body.Center.X-bottom.X;var cy=(double)body.Center.Y-bottom.Y-height*.5;var cz=(double)body.Center.Z-bottom.Z;
        double along;
        if(ax==0 && az==0)along=Math.Clamp(-cy/ay,-body.HalfSegment,body.HalfSegment);
        else if(ay==0)along=Math.Clamp(-(cx*ax+cz*az)/(ax*ax+az*az),-body.HalfSegment,body.HalfSegment);
        else
        {
            // Squared distance to a convex cylinder is convex along a segment.
            // Its monotone derivative locates the minimum, including arbitrary tilted bodies.
            var left=-body.HalfSegment;var right=body.HalfSegment;
            for(var iteration=0;iteration<64;iteration++)
            {
                var middle=(left+right)*.5;var x=cx+ax*middle;var y=cy+ay*middle;var z=cz+az*middle;
                var planar=Math.Sqrt(x*x+z*z);var vertical=y-Math.Clamp(y,-height*.5,height*.5);
                var gradient=vertical*ay+(planar>radius?(planar-radius)*(x*ax+z*az)/planar:0);
                if(gradient==0){left=right=middle;break;}
                if(gradient>0)right=middle;else left=middle;
            }
            along=(left+right)*.5;
        }
        var px=cx+ax*along;var py=cy+ay*along;var pz=cz+az*along;
        var radial=Math.Sqrt(px*px+pz*pz);var scale=radial>radius?radius/radial:1;
        var yInside=Math.Clamp(py,-height*.5,height*.5);
        var radialGap=Math.Max(0,radial-radius);var verticalGap=py-yInside;
        witness=new((float)(bottom.X+px*scale),(float)(bottom.Y+height*.5+yInside),(float)(bottom.Z+pz*scale));
        var squared=body.Radius*body.Radius;
        return radialGap*radialGap+verticalGap*verticalGap<=squared+Math.Max(1,squared)*Math.ScaleB(1d,-44);
    }
}
