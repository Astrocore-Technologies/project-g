using System.Numerics;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class SurfaceNavigationPrecisionTests
{
    [Fact]
    public void LocatorPrefersContainingFaceJustBeyondALongPortal()
    {
        var mesh = Mesh([new(300,0,-400),new(400,0,-400),new(400,0,400),new(500,0,-400)],[[0,1,2],[1,3,2]]);
        Assert.True(mesh.TryLocate(new(400.00003f,0,0),.35f,out var located));
        Assert.Equal(1,located.Triangle);
        Assert.False(mesh.Triangles[0].Sample(400.002,0,out _));
        Assert.True(mesh.Triangles[0].Sample(400.00005,0,out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedTickWalkingCrossesThinSlopedFacesAtLargeCoordinates(bool reverse)
    {
        // Captured from the forest bake: a long, thin pair exposed rounding of
        // portal probes and differences of opposite-sign world coordinates.
        var mesh = Mesh([new(152.306f,7.9f,249.339f),new(158.839f,8.45f,232.71f),
            new(197,12.85f,118),new(157.935f,8.35f,236.774f)],[[0,1,2],[3,2,1]]);
        var grid = new NavigationGrid(new RegionNavigation(new(-480,-400),16,.45f,60,50,new byte[3000]));
        grid.AttachSurface(mesh.Map);
        var edge = mesh.Edges[0].Single().Portal;
        var from = Vector3.Lerp(edge,mesh.Triangles[reverse ? 1 : 0].Center,.2f);
        var to = Vector3.Lerp(edge,mesh.Triangles[reverse ? 0 : 1].Center,.2f);
        var mover = new NavigationMover(grid,new MovementSettings(3.5f,.1f,-480,480,-400,400),
            new NavigationPathfinder(grid),new(from.X,from.Z),from.Y);
        Assert.True(mover.TrySetTarget(new(to.X,to.Z),to.Y));
        for (var tick=0;tick<1000 && mover.IsMoving;tick++) mover.Step(.05f);
        Assert.InRange(Vector3.Distance(mover.Foot,to),0,.002f);
    }

    [Fact]
    public void TraceCrossesAConnectedVertexFanButCannotCrossAMissingOrBlockedFace()
    {
        var vertices = new SurfaceVertex[] { new(272,63.467f,-451),new(271,63.444f,-448),
            new(271,63.25f,-458),new(275,63.55f,-453),new(275,63.52f,-447) };
        var mesh = Mesh(vertices, [[0,1,2],[0,2,3],[0,3,4],[0,4,1]]);
        var from = new Vector3(271.90747f,63.46f,-450.95374f);
        var to = new Vector3(273.68866f,63.49f,-451.84433f);
        Assert.True(mesh.TryLocate(from,.35f,out var start));
        Assert.True(mesh.TryLocate(to,.35f,out var goal));
        Assert.True(mesh.Trace(start,goal));
        Assert.True(mesh.Trace(goal,start));
        mesh.ApplyPatch([goal.Triangle],true);
        Assert.False(mesh.Trace(start,goal));
        var gap = Mesh(vertices, [[0,1,2],[0,3,4]]);
        Assert.True(gap.TryLocate(from,.35f,out start));
        Assert.True(gap.TryLocate(to,.35f,out goal));
        Assert.False(gap.Trace(start,goal));
    }

    private static SurfaceNavigation Mesh(SurfaceVertex[] vertices,int[][] polygons)
    {
        var map = new SurfaceGeometry { ScenePath="res://Scenes/Regions/Precision.tscn",SourceHash=new('A',64),Vertices=vertices,Polygons=polygons };
        map.Seal(); return new(map);
    }
}
