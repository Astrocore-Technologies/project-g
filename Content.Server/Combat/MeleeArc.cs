using System.Numerics;

namespace Content.Server.Combat;

/// <summary>
/// A radial blade swept through a planar arc, thickened by a sphere. The outermost
/// extent is Range, including thickness. Distance to a target's medial segment is
/// solved against the sector interior, radial edges and circular arc, without pose sampling.
/// </summary>
internal readonly partial struct MeleeArc
{
    private readonly record struct Point(double X,double Y,double Z)
    {
        internal static Point From(Vector3 v)=>new(v.X,v.Y,v.Z);
        public static Point operator +(Point a,Point b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static Point operator -(Point a,Point b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static Point operator *(Point a,double scale)=>new(a.X*scale,a.Y*scale,a.Z*scale);
        internal double Squared=>X*X+Y*Y+Z*Z;
        internal static double Dot(Point a,Point b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        internal static Point Cross(Point a,Point b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        internal Point Unit()=>this*(1/Math.Sqrt(Squared));
    }

    private readonly Point _origin,_forward,_right,_normal;
    private readonly double _length,_halfAngle,_radius;
    private readonly record struct Contact(double Squared,Point Blade);

    internal MeleeArc(Vector3 origin,Vector3 direction,Vector2 facing,double range,double arcRadians,double thickness)
    {
        if(!Finite(origin) || !Finite(direction) || direction==Vector3.Zero ||
            !float.IsFinite(facing.X) || !float.IsFinite(facing.Y) || facing==Vector2.Zero ||
            !double.IsFinite(range) || !double.IsFinite(arcRadians) || !double.IsFinite(thickness) ||
            range<=0 || arcRadians<=0 || arcRadians>Math.Tau || thickness<0 || thickness*.5>range)
            throw new ArgumentException("Invalid melee arc geometry.");
        _origin=Point.From(origin);_forward=Point.From(direction).Unit();
        var horizontal=new Point(_forward.Z,0,-_forward.X);
        _right=(horizontal.Squared>0?horizontal:new Point(facing.Y,0,-facing.X)).Unit();
        _normal=Point.Cross(_forward,_right).Unit();
        _radius=thickness*.5;_length=range-_radius;_halfAngle=arcRadians*.5;
    }

    internal bool Intersects(CombatCapsule capsule)
        =>TryContact(capsule,out _);

    internal bool TryContact(CombatCapsule capsule,out Vector3 bladePoint)
    {
        var contact=Closest(capsule);var blade=_origin+_forward*contact.Blade.X+_right*contact.Blade.Y+_normal*contact.Blade.Z;
        bladePoint=new((float)blade.X,(float)blade.Y,(float)blade.Z);
        var radius=capsule.Radius+_radius;var squared=radius*radius;
        return contact.Squared<=squared+Math.Abs(squared)*Math.ScaleB(1d,-44);
    }

    internal double DistanceSquared(CombatCapsule capsule)=>Closest(capsule).Squared;

    private Contact Closest(CombatCapsule capsule)
    {
        var axis=Point.From(capsule.Axis).Unit();
        var start=Point.From(capsule.Center)-axis*capsule.HalfSegment-_origin;
        var motion=axis*(2*capsule.HalfSegment);
        start=Local(start);motion=Local(motion);
        if(_length==0)return SegmentClosest(start,motion,default);
        if(_halfAngle<=Math.PI*.5)return SectorClosest(start,motion,_length,_halfAngle);
        // A wider sweep is the union of two convex sectors. This also covers a full circle.
        var angle=_halfAngle*.5;var (sin,cos)=Math.SinCos(angle);
        var first=SectorClosest(Rotate(start,sin,cos),Rotate(motion,sin,cos),_length,angle);
        var second=SectorClosest(Rotate(start,-sin,cos),Rotate(motion,-sin,cos),_length,angle);
        return first.Squared<=second.Squared ? first with {Blade=Rotate(first.Blade,-sin,cos)}
            : second with {Blade=Rotate(second.Blade,sin,cos)};
    }

    private Point Local(Point p)=>new(Point.Dot(p,_forward),Point.Dot(p,_right),Point.Dot(p,_normal));
    private static Point Rotate(Point p,double sin,double cos)=>new(p.X*cos+p.Y*sin,-p.X*sin+p.Y*cos,p.Z);
    private static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);

    private static Contact SectorClosest(Point p,Point v,double length,double halfAngle)
    {
        var (sin,cos)=Math.SinCos(halfAngle);if(halfAngle==Math.PI*.5)cos=0;
        var left=0d;var right=1d;var best=new Contact(double.PositiveInfinity,default);
        var planarSpeed=v.X*v.X+v.Y*v.Y;var planarDot=p.X*v.X+p.Y*v.Y;var planarSquared=p.X*p.X+p.Y*p.Y;
        // The projected segment above the filled sector: two half-planes and a disk.
        if(Clip(p.X*sin-p.Y*cos,v.X*sin-v.Y*cos,ref left,ref right) &&
            Clip(p.X*sin+p.Y*cos,v.X*sin+v.Y*cos,ref left,ref right) &&
            ClipCircle(planarSpeed,planarDot,planarSquared-length*length,ref left,ref right))
        {
            var t=v.Z==0?left:Math.Clamp(-p.Z/v.Z,left,right);
            var normal=p.Z+v.Z*t;best=new(normal*normal,new(p.X+v.X*t,p.Y+v.Y*t,0));
        }
        var edge=SegmentClosest(p,v,new(length*cos,length*sin,0));if(edge.Squared<best.Squared)best=edge;
        edge=SegmentClosest(p,v,new(length*cos,-length*sin,0));if(edge.Squared<best.Squared)best=edge;

        ArcDistance(p,v,length,cos,0,ref best);ArcDistance(p,v,length,cos,1,ref best);
        if(v.Z!=0)ArcDistance(p,v,length,cos,-p.Z/v.Z,ref best);
        if(planarSpeed>0)ArcDistance(p,v,length,cos,-planarDot/planarSpeed,ref best);
        // Stationary distance to the outer circle satisfies A*sqrt(R)=L*B.
        // Squaring gives degree <=4. Extra squared roots are still valid geometric
        // witnesses, so including them cannot create a spurious intersection.
        var a=Point.Dot(p,v);var b=v.Squared;var l2=length*length;
        var r0=planarSquared;var r1=2*planarDot;var r2=planarSpeed;
        Span<double> polynomial=stackalloc double[5]
        {
            a*a*r0-l2*planarDot*planarDot,
            a*a*r1+2*a*b*r0-2*l2*planarDot*r2,
            a*a*r2+2*a*b*r1+b*b*r0-l2*r2*r2,
            2*a*b*r2+b*b*r1,b*b*r2
        };
        Span<double> roots=stackalloc double[4];var count=CombatPolynomial.Roots(polynomial,roots);
        for(var i=0;i<count;i++)ArcDistance(p,v,length,cos,roots[i],ref best);
        return best with {Squared=Math.Max(0,best.Squared)};
    }

    private static void ArcDistance(Point p,Point v,double length,double cos,double t,ref Contact best)
    {
        if(t<0 || t>1)return;
        var at=p+v*t;var radial=Math.Sqrt(at.X*at.X+at.Y*at.Y);
        if(at.X<radial*cos)return;
        var gap=radial-length;var squared=gap*gap+at.Z*at.Z;
        if(squared<best.Squared)best=new(squared,radial==0?new(length,0,0):new(at.X*length/radial,at.Y*length/radial,0));
    }

    private static bool Clip(double value,double speed,ref double left,ref double right)
    {
        if(speed==0)return value>=0;
        var at=-value/speed;if(speed>0)left=Math.Max(left,at);else right=Math.Min(right,at);
        return left<=right;
    }
    private static bool ClipCircle(double a,double b,double c,ref double left,ref double right)
    {
        if(a==0)return c<=0;
        var discriminant=b*b-a*c;if(discriminant<0)return false;
        var root=Math.Sqrt(discriminant);left=Math.Max(left,(-b-root)/a);right=Math.Min(right,(-b+root)/a);
        return left<=right;
    }

    private static Contact SegmentClosest(Point p,Point v,Point edge)
    {
        var a=v.Squared;var e=edge.Squared;var f=Point.Dot(edge,p);double s,t;
        if(a==0){s=0;t=e==0?0:Math.Clamp(f/e,0,1);}
        else
        {
            var c=Point.Dot(v,p);
            if(e==0){t=0;s=Math.Clamp(-c/a,0,1);}
            else
            {
                var b=Point.Dot(v,edge);var denominator=a*e-b*b;
                s=denominator>0?Math.Clamp((b*f-c*e)/denominator,0,1):0;t=(b*s+f)/e;
                if(t<0){t=0;s=Math.Clamp(-c/a,0,1);}
                else if(t>1){t=1;s=Math.Clamp((b-c)/a,0,1);}
            }
        }
        return new((p+v*s-edge*t).Squared,edge*t);
    }
}
