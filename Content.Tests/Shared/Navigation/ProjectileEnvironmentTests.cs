using System.Numerics;
using Content.Shared.Navigation;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class ProjectileEnvironmentTests
{
    private static SurfaceGeometry Map(SurfaceBox[]? boxes=null,bool triangle=false)
    {
        var map=new SurfaceGeometry{Version=3,ScenePath="res://Scenes/Regions/Test.tscn",SourceHash=new string('A',64),
            Vertices=[new(-10,-5,-10),new(10,-5,-10),new(10,-5,10),new(-10,-5,10),new(0,0,0),new(0,2,0),new(0,0,2)],
            Polygons=[[0,1,2,3]],Occluders=boxes??[],OcclusionFaces=triangle?[[4,5,6]]:[]};
        map.Seal();return map;
    }

    [Fact]public void ActualCenterHeightAndSphereRadiusDetermineTheWallContact()
    {
        var visibility=new SurfaceVisibility(Map([new(new(0,1,0),new(2,2,2),new(0,0,0))]));
        Assert.True(visibility.TryFirstHit(new(-4,1,0),new(4,1,0),.5f,out var at));Assert.Equal(.3125f,at,6);
        Assert.False(visibility.TryFirstHit(new(-4,2.6f,0),new(4,2.6f,0),.5f,out _));
        Assert.True(visibility.TryFirstHit(new(-4,2.4f,0),new(4,2.4f,0),.5f,out _));
    }

    [Fact]public void RoundedCornersDoNotUseTheExpandedBoxAsTheNarrowPhase()
    {
        var visibility=new SurfaceVisibility(Map([new(new(0,1,0),new(2,2,2),new(0,0,0))]));
        Assert.False(visibility.TryFirstHit(new(-4,2.4f,1.4f),new(4,2.4f,1.4f),.5f,out _));
        Assert.True(visibility.TryFirstHit(new(-4,2.3f,1.3f),new(4,2.3f,1.3f),.5f,out var at));
        Assert.Equal((4-1-Math.Sqrt(.25-.18))/8,at,6);
    }

    [Fact]public void RotatedBoxesUseTheirActualOrientation()
    {
        var visibility=new SurfaceVisibility(Map([new(new(0,1,0),new(2,2,2),new(0,MathF.PI/4,0))]));
        Assert.True(visibility.TryFirstHit(new(-4,1,0),new(4,1,0),.5f,out var at));
        Assert.Equal((4-Math.Sqrt(2)-.5)/8,at,6);
    }

    [Fact]public void TriangleFacesEdgesAndVerticesStopTheSphereWithoutRayContact()
    {
        var visibility=new SurfaceVisibility(Map(triangle:true));
        Assert.True(visibility.TryFirstHit(new(-2,.5f,.5f),new(2,.5f,.5f),.25f,out var at));Assert.Equal(.4375f,at,6);
        Assert.True(visibility.TryFirstHit(new(-2,1,1.2f),new(2,1,1.2f),.25f,out at));
        Assert.Equal((2-Math.Sqrt(.0625-.02))/4,at,6);
        Assert.False(visibility.TryFirstHit(new(-2,1,1.4f),new(2,1,1.4f),.25f,out _));
        Assert.True(visibility.TryFirstHit(new(-2,-.15f,-.15f),new(2,-.15f,-.15f),.25f,out at));
        Assert.Equal((2-Math.Sqrt(.0625-.045))/4,at,6);
        Assert.True(visibility.TryFirstHit(new(-.1f,.5f,.5f),new(-.1f,.5f,.5f),.25f,out at));Assert.Equal(0,at);
    }

    [Fact]public void TheRadiusParticipatesInEnvironmentBroadPhaseAcrossTileBorders()
    {
        var visibility=new SurfaceVisibility(Map([new(new(4.25f,1,0),new(.25f,2,2),new(0,0,0))]));
        Assert.True(visibility.TryFirstHit(new(3.9f,1,-4),new(3.9f,1,4),.3f,out _));
    }

    [Theory][InlineData(.1f)][InlineData(.4f)]
    public void ProjectileCollisionDoesNotBorrowTheNavigationAgentRadius(float agentRadius)
    {
        var cells=new byte[400];cells[10*20+10]=1;var grid=new NavigationGrid(new(new(-10,-10),1,agentRadius,20,20,cells));
        Assert.True(grid.TryProjectileObstacle(new(-5,1,.5f),new(5,1,.5f),.5f,out var at));Assert.Equal(.45f,at,6);
        Assert.False(grid.TryProjectileObstacle(new(-.4f,1,-.4f),new(-.4f,1,-.4f),.5f,out _));
    }

    [Fact]public void VisibilityRaysHaveNeitherNavigationPaddingNorAnImplicitHeightOffset()
    {
        var grid=new NavigationGrid(new(new(-10,-10),1,.45f,20,20,new byte[400]));
        grid.AttachSurface(Map([new(new(0,1,0),new(2,2,2),new(0,0,0))]));
        Assert.True(grid.TryVisibilityObstacle(new(-4,1,0),new(4,1,0),out var at));Assert.Equal(.375f,at,6);
        Assert.False(grid.TryVisibilityObstacle(new(-4,2.01f,0),new(4,2.01f,0),out _));
        Assert.True(grid.TryProjectileObstacle(new(-4,2.01f,0),new(4,2.01f,0),.1f,out _));
        Assert.True(grid.TryVisibilityObstacle(new(float.NaN,0,0),Vector3.One,out _));
        Assert.True(grid.TryVisibilityObstacle(Vector3.Zero,new(129,0,0),out _));
        var cells=new byte[400];cells[10*20+10]=1;
        var flat=new NavigationGrid(new(new(-10,-10),1,.4f,20,20,cells));
        Assert.False(flat.TryVisibilityObstacle(new(-4,1,-.01f),new(4,1,-.01f),out _));
        Assert.True(flat.TryVisibilityObstacle(new(-4,1,.01f),new(4,1,.01f),out at));Assert.Equal(.5f,at,6);
    }
}
