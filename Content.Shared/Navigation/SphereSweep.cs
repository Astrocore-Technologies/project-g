using System.Numerics;

namespace Content.Shared.Navigation;

/// <summary>Linear sphere sweeps against public environment geometry. No sampling or navigation-body inflation.</summary>
internal static class SphereSweep
{
    internal static bool Box(Vector3 start,Vector3 travel,Vector3 half,double radius,out float fraction)
    {
        Span<double> cuts=stackalloc double[8];cuts[0]=0;cuts[1]=1;var count=2;
        for(var axis=0;axis<3;axis++)
        {
            var speed=(double)travel[axis];if(speed==0)continue;
            for(var sign=-1;sign<=1;sign+=2)
            {
                var t=(sign*(double)half[axis]-start[axis])/speed;
                if(t>0 && t<1)cuts[count++]=t;
            }
        }
        cuts[..count].Sort();fraction=1;
        for(var i=0;i<count-1;i++)
        {
            var left=cuts[i];var right=cuts[i+1];var middle=(left+right)*.5;
            var a=0d;var b=0d;var c=-radius*radius;
            for(var axis=0;axis<3;axis++)
            {
                var speed=(double)travel[axis];var at=start[axis]+speed*middle;var edge=(double)half[axis];
                if(at>=-edge && at<=edge)continue;
                var offset=start[axis]+speed*left-(at<0?-edge:edge);
                a+=speed*speed;b+=offset*speed;c+=offset*offset;
            }
            if(Entry(a,b,c,right-left,out var hit)){fraction=(float)(left+hit);return true;}
        }
        return false;
    }

    internal static bool Triangle(Vector3 start,Vector3 travel,Vector3 a,Vector3 b,Vector3 c,double radius,out float fraction)
    {
        var normal=Vector3.Cross(b-a,c-a);var length=normal.Length();fraction=1;
        if(length==0)return false;
        normal/=length;var distance=Dot(start-a,normal);var speed=Dot(travel,normal);
        var found=false;
        var projected=start-normal*(float)distance;
        if(Math.Abs(distance)<=radius && InTriangle(projected,a,b,c,normal)){fraction=0;return true;}
        if(speed!=0)
            for(var sign=-1;sign<=1;sign+=2)
            {
                var t=(sign*radius-distance)/speed;
                if(t<0 || t>fraction)continue;
                projected=start+travel*(float)t-normal*(float)(sign*radius);
                if(InTriangle(projected,a,b,c,normal)){found=true;fraction=(float)t;}
            }
        if(Edge(start,travel,a,b,radius,out var edge) && edge<=fraction){found=true;fraction=edge;}
        if(Edge(start,travel,b,c,radius,out edge) && edge<=fraction){found=true;fraction=edge;}
        if(Edge(start,travel,c,a,radius,out edge) && edge<=fraction){found=true;fraction=edge;}
        return found;
    }

    private static bool InTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c,Vector3 normal)
        =>Dot(Vector3.Cross(b-a,p-a),normal)>=0 && Dot(Vector3.Cross(c-b,p-b),normal)>=0 && Dot(Vector3.Cross(a-c,p-c),normal)>=0;

    private static bool Edge(Vector3 start,Vector3 travel,Vector3 from,Vector3 to,double radius,out float fraction)
    {
        var axis=to-from;var length=Math.Sqrt(Dot(axis,axis));var offset=start-from;fraction=1;var found=false;
        if(Sphere(offset,travel,radius,out var at)){found=true;fraction=(float)at;}
        if(Sphere(start-to,travel,radius,out at) && at<=fraction){found=true;fraction=(float)at;}
        if(length==0)return found;
        var ax=axis.X/length;var ay=axis.Y/length;var az=axis.Z/length;
        var along=offset.X*ax+offset.Y*ay+offset.Z*az;var speed=travel.X*ax+travel.Y*ay+travel.Z*az;
        var x=offset.X-ax*along;var y=offset.Y-ay*along;var z=offset.Z-az*along;
        var vx=travel.X-ax*speed;var vy=travel.Y-ay*speed;var vz=travel.Z-az*speed;
        if(Entry(vx*vx+vy*vy+vz*vz,x*vx+y*vy+z*vz,x*x+y*y+z*z-radius*radius,1,out at) &&
            along+speed*at>=0 && along+speed*at<=length && at<=fraction){found=true;fraction=(float)at;}
        return found;
    }

    private static bool Sphere(Vector3 offset,Vector3 travel,double radius,out double at)
        =>Entry(Dot(travel,travel),Dot(offset,travel),Dot(offset,offset)-radius*radius,1,out at);

    private static bool Entry(double a,double b,double c,double horizon,out double at)
    {
        at=0;if(c<=0)return true;
        if(a==0 || b>=0)return false;
        var discriminant=b*b-a*c;if(discriminant<0)return false;
        at=c/(-b+Math.Sqrt(discriminant));return at>=0 && at<=horizon;
    }
    private static double Dot(Vector3 a,Vector3 b)=>(double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z;
}
