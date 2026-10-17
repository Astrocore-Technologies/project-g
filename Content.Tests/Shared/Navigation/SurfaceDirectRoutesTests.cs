using System.Numerics;
using System.Text.Json;
using Content.Shared.Navigation;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class SurfaceDirectRoutesTests
{
    [Fact]
    public void ClearSurfaceCrossesPortalsAtTheStraightLineInsteadOfTheirMidpoints()
    {
        var mesh=Mesh([new(0,0,0),new(20,0,0),new(0,0,10),new(20,0,10)],[[0,1,2],[1,3,2]]);
        var start=new Vector3(1,0,1);var goal=new Vector3(19,0,7);var route=new List<Vector3>();
        Assert.Equal(SearchStatus.Success,new SurfacePathfinder(mesh).Find(start,goal,mesh.Revision,route));
        AssertStraight(route,start,goal);
        mesh.ApplyPatch([1],true);
        Assert.NotEqual(SearchStatus.Success,new SurfacePathfinder(mesh).Find(start,goal,mesh.Revision,route));
    }

    [Fact]
    public void StraightHorizontalRouteRetainsRidgeHeightAndNeverCutsThroughTerrain()
    {
        var mesh=Mesh([new(-2,0,-2),new(0,2,-2),new(0,2,2),new(-2,0,2),new(2,0,-2),new(2,0,2)],[[0,1,2,3],[1,4,5,2]]);
        var route=new List<Vector3>();
        Assert.Equal(SearchStatus.Success,new SurfacePathfinder(mesh).Find(new(-1.5f,.5f,0),new(1.5f,.5f,0),mesh.Revision,route));
        Assert.Contains(route,p=>Math.Abs(p.X)<.001f && Math.Abs(p.Y-2)<.001f);
        Assert.All(route,p=>Assert.InRange(Math.Abs(p.Z),0,.001f));
    }

    [Theory]
    [InlineData(-425,330,-414,321)]
    [InlineData(-425,330,-418,325)]
    [InlineData(-418,325,-414,321)]
    public void BeechSpawnRoadHasNoArtificialDetour(float x,float z,float toX,float toZ)
    {
        var maps=JsonSerializer.Deserialize<Dictionary<string,SurfaceGeometry>>(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Data","Regions","surface-package.json")),SurfaceGeometry.Json)!;
        var mesh=new SurfaceNavigation(maps["linvel_beech"]);var samples=new List<SurfaceLocation>();
        mesh.SampleColumn(x,z,samples);var start=samples.OrderBy(p=>p.Position.Y).First().Position;
        mesh.SampleColumn(toX,toZ,samples);var goal=samples.OrderBy(p=>p.Position.Y).First().Position;
        var route=new List<Vector3>();
        Assert.Equal(SearchStatus.Success,new SurfacePathfinder(mesh).Find(start,goal,mesh.Revision,route));
        AssertStraight(route,start,goal);
    }

    private static void AssertStraight(List<Vector3> route,Vector3 start,Vector3 goal)
    {
        Assert.Equal(start,route[0]);Assert.Equal(goal,route[^1]);
        var direction=new Vector2(goal.X-start.X,goal.Z-start.Z);
        foreach(var point in route)
        {
            var delta=new Vector2(point.X-start.X,point.Z-start.Z);
            var distance=Math.Abs((double)delta.X*direction.Y-(double)delta.Y*direction.X)/direction.Length();
            Assert.InRange(distance,0,.0001);
        }
    }

    private static SurfaceNavigation Mesh(SurfaceVertex[] vertices,int[][] polygons)
    {
        var map=new SurfaceGeometry {ScenePath="res://Scenes/Regions/DirectRoutes.tscn",SourceHash=new('A',64),Vertices=vertices,Polygons=polygons};
        map.Seal();return new(map);
    }
}
