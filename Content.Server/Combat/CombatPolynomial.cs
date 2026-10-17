namespace Content.Server.Combat;

/// <summary>Bounded real polynomial geometry, degree at most four; roots are isolated on (0,1).</summary>
internal static class CombatPolynomial
{
    internal static double Evaluate(ReadOnlySpan<double> p,double x)
    {var value=p[^1];for(var i=p.Length-2;i>=0;i--)value=Math.FusedMultiplyAdd(value,x,p[i]);return value;}

    internal static double Bisect(ReadOnlySpan<double> p,double left,double right,double atLeft)
    {
        for(var i=0;i<60;i++)
        {
            var middle=(left+right)*.5;if(middle==left || middle==right)break;
            var value=Evaluate(p,middle);
            if(value==0)return middle;
            if((value>0)==(atLeft>0)){left=middle;atLeft=value;}else right=middle;
        }
        return right;
    }

    internal static int Roots(ReadOnlySpan<double> p,Span<double> roots)
    {
        while(p.Length>1 && p[^1]==0)p=p[..^1];
        if(p.Length==1)return 0;
        if(p.Length==2)
        {var x=-p[0]/p[1];if(x>0 && x<1){roots[0]=x;return 1;}return 0;}
        Span<double> derivative=stackalloc double[4];
        for(var i=1;i<p.Length;i++)derivative[i-1]=p[i]*i;
        Span<double> critical=stackalloc double[3];var criticalCount=Roots(derivative[..(p.Length-1)],critical);
        var count=0;var left=0d;var previous=Evaluate(p,0);
        var scale=0d;foreach(var coefficient in p)scale+=Math.Abs(coefficient);
        var rounding=scale*Math.ScaleB(1d,-48);
        for(var i=0;i<=criticalCount;i++)
        {
            var right=i<criticalCount?critical[i]:1;var value=Evaluate(p,right);
            if(Math.Abs(value)<=rounding){value=0;if(right<1)roots[count++]=right;}
            else if(previous!=0 && value!=0 && (previous>0)!=(value>0))roots[count++]=Bisect(p,left,right,previous);
            left=right;previous=value;
        }
        return count;
    }
}
