using System.Numerics;
using Content.Shared.Navigation;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class SurfaceCliffVisibilityTests
{
    private static SurfaceGeometry Cliff()
    {
        var map=new SurfaceGeometry { Version=3, WalkableFacesOcclude=true,
            ScenePath="res://Scenes/Regions/cliff.tscn",SourceHash=new string('A',64),
            Vertices=[new(-5,0,-5),new(5,0,-5),new(5,0,5),new(-5,0,5),
                new(0,0,-2),new(0,3,-2),new(0,3,2),new(0,0,2)],
            Polygons=[[0,1,2,3]],OcclusionFaces=[[4,5,6],[4,6,7]] };
        map.Seal();return map;
    }

    [Fact]
    public void VerticalNativeCliffBlocksAttacksWithoutBecomingASecondWalkableFloor()
    {
        var map=SurfaceGeometry.Decode(Cliff().Encode());
        var navigation=new SurfaceNavigation(map);
        var visibility=new SurfaceVisibility(map);
        Assert.Equal(2,navigation.Triangles.Length);
        Assert.False(navigation.TryLocate(new(0,2,0),.35f,out _));
        Assert.False(visibility.Clear(new(-4,1,0),new(4,1,0)));
        Assert.False(visibility.Clear(new(4,1,0),new(-4,1,0)));
        Assert.True(visibility.Clear(new(-4,4,0),new(4,4,0)));
        Assert.True(visibility.Clear(new(-4,1,3),new(4,1,3)));
        map.OcclusionFaces![0][1]=7;
        Assert.Throws<InvalidDataException>(()=>SurfaceGeometry.Decode(map.Encode()));
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("missingVertex")]
    [InlineData("degenerate")]
    [InlineData("tooManyFaces")]
    public void StaticCliffInputIsBoundedAndValidatedBeforeVisibilityIndexing(string change)
    {
        var map=Cliff();
        switch(change)
        {
            case "legacy": map.Version=2;break;
            case "missingVertex": map.OcclusionFaces![0][0]=map.Vertices.Length;break;
            case "degenerate": map.OcclusionFaces![0]=[4,5,4];break;
            case "tooManyFaces": map.OcclusionFaces=Enumerable.Repeat(new[]{4,5,6},SurfaceGeometry.MaxPolygons).ToArray();break;
        }
        Assert.Throws<InvalidDataException>(map.Validate);
    }
}
