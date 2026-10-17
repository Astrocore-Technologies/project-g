namespace Content.Server.Data;

/// <summary>Authorable basic blade-sweep volume. Thickness is the full diameter of its rounded envelope.</summary>
public sealed record MeleeArcDefinition
{
    public required float ArcDegrees {get;init;}
    public required float Thickness {get;init;}
    public required float OriginHeight {get;init;}
    internal float HalfAngleRadians=>ArcDegrees*MathF.PI/360;

    internal void Validate(string path,double range)
    {
        if(!float.IsFinite(ArcDegrees) || ArcDegrees is <=0 or >360 ||
            !float.IsFinite(Thickness) || Thickness<=0 || Thickness>128 || Thickness*.5>range ||
            !float.IsFinite(OriginHeight) || OriginHeight is <0 or >128)
            throw new ArgumentException($"{path}: invalid arc, thickness, origin height or thickness exceeding reach.");
    }
}
