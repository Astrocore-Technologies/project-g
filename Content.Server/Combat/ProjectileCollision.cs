using System.Numerics;
using static Content.Server.Combat.CombatPolynomial;

namespace Content.Server.Combat;

/// <summary>Earliest sphere contact against linear or quadratic relative motion, without sampling in time.</summary>
internal static class ProjectileCollision
{
    /// <summary>Continuous sphere/capsule contact. The capsule translates with the supplied relative motion.</summary>
    internal static bool TryCapsuleTime(double x,double y,double z,Vector3 velocity,Vector3 acceleration,
        CombatCapsule capsule,double projectileRadius,double seconds,out double time)
    {
        if(!double.IsFinite(seconds) || seconds<0 || !double.IsFinite(projectileRadius) || projectileRadius<0)
            throw new ArgumentException("Invalid collision horizon or radius.");
        var radius=capsule.Radius+projectileRadius;time=0;
        if(capsule.DistanceSquared(x,y,z)<=radius*radius)return true;
        if(seconds==0)return false;
        var axis=capsule.Axis;var length=Math.Sqrt(Dot(axis,axis));
        var ax=axis.X/length;var ay=axis.Y/length;var az=axis.Z/length;var half=capsule.HalfSegment;
        var found=false;time=double.PositiveInfinity;
        // The two end spheres include their common seams with the finite cylinder.
        for(var end=-1;end<=1;end+=2)
            if(TryTime(x-end*ax*half,y-end*ay*half,z-end*az*half,velocity,acceleration,radius,seconds,out var hit) && hit<time)
            {found=true;time=hit;}
        if(half==0)return found;
        var along=x*ax+y*ay+z*az;
        var speed=velocity.X*ax+velocity.Y*ay+velocity.Z*az;
        var change=acceleration.X*ax+acceleration.Y*ay+acceleration.Z*az;
        var px=x-ax*along;var py=y-ay*along;var pz=z-az*along;
        var vx=velocity.X-ax*speed;var vy=velocity.Y-ay*speed;var vz=velocity.Z-az*speed;
        var gx=acceleration.X-ax*change;var gy=acceleration.Y-ay*change;var gz=acceleration.Z-az*change;
        var h=seconds;var h2=h*h;
        Span<double> p=stackalloc double[5]
        {
            px*px+py*py+pz*pz-radius*radius,
            2*(px*vx+py*vy+pz*vz)*h,
            (vx*vx+vy*vy+vz*vz+px*gx+py*gy+pz*gz)*h2,
            (vx*gx+vy*gy+vz*gz)*h2*h,
            .25*(gx*gx+gy*gy+gz*gz)*h2*h2
        };
        Span<double> roots=stackalloc double[4];var count=Roots(p,roots);
        // All cylinder crossings matter: an earlier infinite-cylinder root may lie beyond an end.
        for(var i=0;i<count;i++)
        {
            var hit=roots[i]*seconds;var at=along+speed*hit+.5*change*hit*hit;
            if(Math.Abs(at)<=half && hit<time){found=true;time=hit;}
        }
        var t=seconds;var t2=.5*t*t;
        if(t<time && capsule.DistanceSquared(x+velocity.X*t+acceleration.X*t2,
            y+velocity.Y*t+acceleration.Y*t2,z+velocity.Z*t+acceleration.Z*t2)<=radius*radius)
        {found=true;time=t;}
        return found;
    }

    internal static bool TryTime(Vector3 offset,Vector3 velocity,Vector3 acceleration,double radius,double seconds,out double time)
        =>TryTime(offset.X,offset.Y,offset.Z,velocity,acceleration,radius,seconds,out time);

    internal static bool TryTime(double x,double y,double z,Vector3 velocity,Vector3 acceleration,double radius,double seconds,out double time)
    {
        if(!double.IsFinite(seconds) || seconds<0 || !double.IsFinite(radius) || radius<0)
            throw new ArgumentException("Invalid collision horizon or radius.");
        time=0;var c=x*x+y*y+z*z-radius*radius;
        if(c<=0)return true;
        if(seconds==0)return false;
        if(acceleration==Vector3.Zero)
        {
            var a=Dot(velocity,velocity);var b=x*velocity.X+y*velocity.Y+z*velocity.Z;
            if(a==0 || b>=0)return false;
            var discriminant=b*b-a*c;if(discriminant<0)return false;
            time=c/(-b+Math.Sqrt(discriminant));return time>=0 && time<=seconds;
        }
        // Normalize the horizon to [0,1]. Critical points of the quartic split it
        // into monotone intervals, so neither a crossing nor a tangent can tunnel.
        var h=seconds;var h2=h*h;
        Span<double> p=stackalloc double[5];
        p[0]=c;p[1]=2*(x*velocity.X+y*velocity.Y+z*velocity.Z)*h;
        p[2]=(Dot(velocity,velocity)+x*acceleration.X+y*acceleration.Y+z*acceleration.Z)*h2;
        p[3]=Dot(velocity,acceleration)*h2*h;p[4]=.25*Dot(acceleration,acceleration)*h2*h2;
        Span<double> derivative=stackalloc double[4];
        for(var i=1;i<5;i++)derivative[i-1]=p[i]*i;
        Span<double> critical=stackalloc double[3];var count=Roots(derivative,critical);
        var left=0d;var previous=p[0];var scale=0d;foreach(var value in p)scale+=Math.Abs(value);
        var rounding=scale*Math.ScaleB(1d,-48);
        for(var i=0;i<=count;i++)
        {
            var right=i<count?critical[i]:1;var value=Evaluate(p,right);
            if(value<=0)
            {time=Bisect(p,left,right,previous)*seconds;return true;}
            if(i<count && Math.Abs(value)<=rounding){time=right*seconds;return true;}
            left=right;previous=value;
        }
        return false;
    }

    private static double Dot(Vector3 a,Vector3 b)=>(double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z;
}
