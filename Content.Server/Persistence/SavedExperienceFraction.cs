using System.Globalization;
using System.Numerics;

namespace Content.Server.Persistence;

/// <summary>Exact, reduced remainder of EXP. Decimal strings keep JSON independent of BigInteger serialization.</summary>
public sealed record SavedExperienceFraction(int Version,string Numerator,string Denominator)
{
    // Presentation only. Authoritative awards never pass through floating point.
    public double Display()=>(double)(BigInteger.Parse(Numerator,CultureInfo.InvariantCulture)*1000000000000L/
        BigInteger.Parse(Denominator,CultureInfo.InvariantCulture))/1000000000000d;
    public void Validate()
    {
        static bool Digits(string value)=>value is {Length:>0 and <=512} &&
            (value.Length==1 || value[0]!='0') && value.All(c=>c is >= '0' and <= '9');
        if(Version!=1 || !Digits(Numerator) || !Digits(Denominator))throw new InvalidDataException("Invalid EXP fraction encoding.");
        var n=BigInteger.Parse(Numerator,CultureInfo.InvariantCulture);var d=BigInteger.Parse(Denominator,CultureInfo.InvariantCulture);
        if(n<0 || d<=0 || n>=d || BigInteger.GreatestCommonDivisor(n,d)!=1)throw new InvalidDataException("Invalid EXP fraction.");
    }
    public static (long Whole,SavedExperienceFraction? Remainder) Add(SavedExperienceFraction? previous,int pool,int recipients)
    {
        if(pool<=0 || recipients<=0)throw new ArgumentOutOfRangeException(nameof(pool));
        previous?.Validate();
        var n=previous is null?BigInteger.Zero:BigInteger.Parse(previous.Numerator,CultureInfo.InvariantCulture);
        var d=previous is null?BigInteger.One:BigInteger.Parse(previous.Denominator,CultureInfo.InvariantCulture);
        n=n*recipients+(BigInteger)pool*d;d*=recipients;
        var whole=BigInteger.DivRem(n,d,out var remainder);
        if(whole>long.MaxValue)throw new InvalidDataException("EXP award exceeds storage budget.");
        if(remainder.IsZero)return ((long)whole,null);
        var gcd=BigInteger.GreatestCommonDivisor(remainder,d);
        var saved=new SavedExperienceFraction(1,(remainder/gcd).ToString(CultureInfo.InvariantCulture),(d/gcd).ToString(CultureInfo.InvariantCulture));
        saved.Validate();return ((long)whole,saved);
    }
}
