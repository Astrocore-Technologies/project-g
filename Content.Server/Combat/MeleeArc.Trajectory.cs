using System.Numerics;

namespace Content.Server.Combat;

internal readonly partial struct MeleeArc
{
    /// <summary>Earliest left-to-right radial blade contact, including tangent contacts without angular sampling.</summary>
    internal bool TryFirstContact(CombatCapsule capsule,out double progress,out Vector3 bladePoint)
    {
        var axis=Local(Point.From(capsule.Axis).Unit());var center=Local(Point.From(capsule.Center)-_origin);
        var start=center-axis*capsule.HalfSegment;var motion=axis*(2*capsule.HalfSegment);
        var radius=capsule.Radius+_radius;var squared=radius*radius;var best=double.PositiveInfinity;
        AcceptAngle(-_halfAngle,start,motion,_length,_halfAngle,squared,ref best);
        if(!double.IsFinite(best))
        {
            // End-sphere silhouettes and their intersections with the end of the radial blade.
            SphereAngles(start,radius,start,motion,_length,_halfAngle,squared,ref best);
            SphereAngles(start+motion,radius,start,motion,_length,_halfAngle,squared,ref best);
            // Interior cylinder silhouette: (C dot (D cross A))² = R² |D cross A|².
            var u=center.Z*axis.Y-center.Y*axis.Z;var v=center.X*axis.Z-center.Z*axis.X;
            var a=u*u-squared*(axis.Z*axis.Z+axis.Y*axis.Y);
            var b=u*v+squared*axis.X*axis.Y;var c=v*v-squared*(axis.Z*axis.Z+axis.X*axis.X);
            var offset=(a+c)*.5;var cosine=(a-c)*.5;var amplitude=Math.Sqrt(cosine*cosine+b*b);
            if(amplitude>0 && Math.Abs(offset)<=amplitude)
            {
                var phase=Math.Atan2(b,cosine);var opening=Math.Acos(Math.Clamp(-offset/amplitude,-1,1));
                for(var sign=-1;sign<=1;sign+=2)
                {
                    var angle=(phase+sign*opening)*.5;
                    AcceptAngle(angle,start,motion,_length,_halfAngle,squared,ref best);
                    AcceptAngle(angle+Math.PI,start,motion,_length,_halfAngle,squared,ref best);
                }
            }
            // The end of the blade meets a cylinder: a quartic after tan(theta/2).
            // Four quarter-turns keep the rational parameter in [0,1], including vertical asymptotes.
            Span<double> polynomial=stackalloc double[5];Span<double> roots=stackalloc double[4];
            for(var quadrant=0;quadrant<4;quadrant++)
            {
                var angle=-Math.PI+quadrant*Math.PI*.5;var (sin,cos)=Math.SinCos(angle);
                var q=new Point(center.X*cos+center.Y*sin,-center.X*sin+center.Y*cos,center.Z);
                var n=new Point(axis.X*cos+axis.Y*sin,-axis.X*sin+axis.Y*cos,axis.Z);
                var p0=new Point(_length-q.X,-q.Y,-q.Z);var p1=new Point(0,2*_length,0);var p2=new Point(-_length-q.X,-q.Y,-q.Z);
                var d0=Point.Dot(p0,n);var d1=Point.Dot(p1,n);var d2=Point.Dot(p2,n);
                polynomial[0]=p0.Squared-d0*d0-squared;
                polynomial[1]=2*(Point.Dot(p0,p1)-d0*d1);
                polynomial[2]=p1.Squared+2*Point.Dot(p0,p2)-d1*d1-2*d0*d2-2*squared;
                polynomial[3]=2*(Point.Dot(p1,p2)-d1*d2);
                polynomial[4]=p2.Squared-d2*d2-squared;
                var count=CombatPolynomial.Roots(polynomial,roots);
                for(var i=0;i<count;i++)AcceptAngle(angle+2*Math.Atan(roots[i]),start,motion,_length,_halfAngle,squared,ref best);
                AcceptAngle(angle,start,motion,_length,_halfAngle,squared,ref best);
                AcceptAngle(angle+Math.PI*.5,start,motion,_length,_halfAngle,squared,ref best);
            }
        }
        progress=double.IsFinite(best)?Math.Clamp((best+_halfAngle)/(2*_halfAngle),0,1):0;
        var closest=double.IsFinite(best)?SegmentClosest(start,motion,new(_length*Math.Cos(best),_length*Math.Sin(best),0)).Blade:default;
        var world=_origin+_forward*closest.X+_right*closest.Y;bladePoint=new((float)world.X,(float)world.Y,(float)world.Z);
        return double.IsFinite(best);
    }

    private static void SphereAngles(Point sphere,double radius,Point start,Point motion,double length,double halfAngle,double squared,ref double best)
    {
        var planar=Math.Sqrt(sphere.X*sphere.X+sphere.Y*sphere.Y);var disk=radius*radius-sphere.Z*sphere.Z;
        if(planar==0 || disk<0)return;
        var middle=Math.Atan2(sphere.Y,sphere.X);
        if(disk<=planar*planar)
        {
            var opening=Math.Asin(Math.Clamp(Math.Sqrt(disk)/planar,0,1));
            AcceptAngle(middle-opening,start,motion,length,halfAngle,squared,ref best);
            AcceptAngle(middle+opening,start,motion,length,halfAngle,squared,ref best);
        }
        if(length<=0)return;
        var cosine=(length*length+sphere.Squared-radius*radius)/(2*length*planar);
        if(Math.Abs(cosine)>1)return;
        var edge=Math.Acos(Math.Clamp(cosine,-1,1));
        AcceptAngle(middle-edge,start,motion,length,halfAngle,squared,ref best);
        AcceptAngle(middle+edge,start,motion,length,halfAngle,squared,ref best);
    }

    private static void AcceptAngle(double angle,Point start,Point motion,double length,double halfAngle,double squared,ref double best)
    {
        angle=Math.IEEERemainder(angle,Math.Tau);
        if(angle < -halfAngle-1e-12 || angle>halfAngle+1e-12)return;
        angle=Math.Clamp(angle,-halfAngle,halfAngle);if(angle>=best)return;
        var edge=new Point(length*Math.Cos(angle),length*Math.Sin(angle),0);
        if(SegmentClosest(start,motion,edge).Squared<=squared+Math.Max(1,squared)*Math.ScaleB(1d,-40))best=angle;
    }
}
