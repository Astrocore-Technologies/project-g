using System.Numerics;
using System.Text;
namespace Content.Server.StarterZone;
public sealed record StarterLandmark(ushort Id,string Name,float X,float Z)
{
    public Vector2 Position => new(X,Z);
}
public sealed record StarterZoneDefinition
{
    public required string RegionName { get; init; }
    public required string TownName { get; init; }
    public required string GuideName { get; init; }
    public required float TownX { get; init; }
    public required float TownZ { get; init; }
    public required float GuideX { get; init; }
    public required float GuideZ { get; init; }
    public required int RevealRadiusCells { get; init; }
    public required StarterLandmark[] Landmarks { get; init; }
    public Vector2 TownPosition => new(TownX,TownZ);
    public Vector2 GuidePosition => new(GuideX,GuideZ);
    public void Validate()
    {
        static bool Name(string s) => !string.IsNullOrWhiteSpace(s) && s.Length<=32 && Encoding.UTF8.GetByteCount(s)<=96 && !s.Any(char.IsControl);
        if(!Name(RegionName) || !Name(TownName) || !Name(GuideName) || !float.IsFinite(TownX) || !float.IsFinite(TownZ) || !float.IsFinite(GuideX) || !float.IsFinite(GuideZ) || RevealRadiusCells is < 1 or > 4 || Landmarks is null || Landmarks.Length!=2 || Landmarks.Any(l=>l is null || !Name(l.Name) || !float.IsFinite(l.X) || !float.IsFinite(l.Z)) || Landmarks[0].Id!=1 || Landmarks[1].Id!=2 || Landmarks[0].Position==Landmarks[1].Position) throw new InvalidDataException("Invalid starter zone definition.");
    }
}
