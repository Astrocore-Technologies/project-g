using System.Numerics;
using Content.Shared.Navigation;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class SurfaceTerrainVisibilityTests
{
    [Fact]
    public void HillBlocksLowAttacksButAllowsRaysAboveItsCrest()
    {
        var map = Map([new(-10,0,-5),new(0,5,-5),new(10,0,-5),new(-10,0,5),new(0,5,5),new(10,0,5)],
            [[0,1,4,3],[1,2,5,4]]);
        var visibility = new SurfaceVisibility(map);
        Assert.False(visibility.Clear(new(-8,2,0),new(8,2,0)));
        Assert.False(visibility.Clear(new(8,2,0),new(-8,2,0)));
        Assert.True(visibility.Clear(new(-8,6,0),new(8,6,0)));
        Assert.True(visibility.Clear(new(-8,2,0),new(-6,3,0)));
    }

    [Fact]
    public void SurfaceDoesNotBlockOrdinaryBodyHeightOrInvalidateRepeatedQueries()
    {
        var map = Map([new(-20,0,-20),new(20,0,-20),new(20,0,20),new(-20,0,20)], [[0,1,2,3]]);
        var visibility = new SurfaceVisibility(map);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(visibility.Clear(new(-8,1,0),new(8,1,0)));
            Assert.False(visibility.Clear(new(0,-1,0),new(0,1,0)));
        }
        Assert.False(visibility.Clear(new(float.NaN,1,0),Vector3.Zero));
    }

    private static SurfaceGeometry Map(SurfaceVertex[] vertices, int[][] polygons)
    {
        var map = new SurfaceGeometry { Version=2,WalkableFacesOcclude=true,ScenePath="res://Scenes/Regions/Test/Test.tscn", SourceHash=new string('A',64),
            Vertices=vertices,Polygons=polygons };
        map.Seal(); return map;
    }

    [Fact]
    public void TerrainFlagIsVersionedAndIncludedInContentIntegrity()
    {
        var map = Map([new(-20,0,-20),new(20,0,-20),new(20,0,20),new(-20,0,20)], [[0,1,2,3]]);
        Assert.True(SurfaceGeometry.Decode(map.Encode()).WalkableFacesOcclude);
        map.Version=1;
        Assert.Throws<InvalidDataException>(map.Validate);
        map.Version=2;
        var revision=map.Revision;
        map.WalkableFacesOcclude=false;
        Assert.Throws<InvalidDataException>(()=>SurfaceGeometry.Decode(map.Encode()));
        map.Seal(); Assert.NotEqual(revision,map.Revision);
        map.Version=1; map.Seal();
        var encoded=map.Encode();
        Assert.DoesNotContain("WalkableFacesOcclude", System.Text.Encoding.UTF8.GetString(encoded));
        Assert.False(SurfaceGeometry.Decode(encoded).WalkableFacesOcclude);
    }
}
