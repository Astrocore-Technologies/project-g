using System.Numerics;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class CombatMotionGeometryTests
{
    [Fact]public void AvoidanceUsesTheSameSegmentForMotionAndCollisionWithoutConsumingTheRoute()
    {
        var grid=new NavigationGrid(new(new(-10,-10),1,.25f,20,20,new byte[400]));
        var mover=new NavigationMover(grid,new(4,.1f,-10,10,-10,10),new(grid),new(-5,0));
        Assert.True(mover.TrySetTarget(new(5,0)));mover.SetAvoidance(new(0,.35f),.5f);
        var before=mover.Foot;var segment=mover.CurrentSegment();Assert.True(segment.Velocity.Z>0);
        Assert.True(segment.Velocity.X>0);Assert.Equal(4,segment.Velocity.Length(),5);
        mover.Step(.05f);Assert.InRange(Vector3.Distance(before+segment.Velocity*.05f,mover.Foot),0,.00001f);
        Assert.Equal(new Vector2(5,0),mover.Target);
    }

    [Fact]public void BlockedAvoidanceFallsBackToTheExistingTraversableRoute()
    {
        var cells=new byte[400];cells[11*20+10]=1;
        var grid=new NavigationGrid(new(new(-10,-10),1,.25f,20,20,cells));
        var mover=new NavigationMover(grid,new(4,.1f,-10,10,-10,10),new(grid),new(-1,.5f));
        Assert.True(mover.TrySetTarget(new(5,.5f)));mover.SetAvoidance(new(0,.5f),1);
        var segment=mover.CurrentSegment();Assert.Equal(0,segment.Velocity.Z);Assert.Equal(4,segment.Velocity.X);
        mover.Step(.05f);Assert.Equal(.5f,mover.Position.Y);Assert.True(mover.IsMoving);
    }

    [Fact]public void ReadingTheCurrentRouteSegmentDoesNotAdvanceOrRepathMovement()
    {
        var grid=new NavigationGrid(new(new(-10,-10),1,.25f,20,20,new byte[400]));
        var mover=new NavigationMover(grid,new(4,.1f,-10,10,-10,10),new(grid),new(-5,0));
        Assert.True(mover.TrySetTarget(new(5,0)));var before=mover.Position;var segment=mover.CurrentSegment(.5f);
        Assert.Equal(before,mover.Position);Assert.Equal(new Vector3(2,0,0),segment.Velocity);
        mover.Step(.125f);Assert.InRange(Vector2.Distance(mover.Position,before+new Vector2(segment.Velocity.X,segment.Velocity.Z)*.25f),0,.00001f);
    }
    [Fact]public void SurfaceSegmentsPreserveTheThreeDimensionalSlope()
    {
        var map=new SurfaceGeometry{ScenePath="res://Scenes/Regions/Test.tscn",SourceHash=new string('A',64),AgentRadius=.25f,
            Vertices=[new(-5,0,-5),new(5,5,-5),new(5,5,5),new(-5,0,5)],Polygons=[[0,1,2,3]]};map.Seal();
        var grid=new NavigationGrid(new(new(-10,-10),1,.25f,20,20,new byte[400]));grid.AttachSurface(map);
        var mover=new NavigationMover(grid,new(4,.1f,-10,10,-10,10),new(grid),new(-4,0),.5f);
        Assert.True(mover.TrySetTarget(new(4,0),4.5f));var before=mover.Foot;var segment=mover.CurrentSegment();
        Assert.True(segment.Velocity.Y>0);mover.Step(.125f);
        Assert.InRange(Vector3.Distance(before+segment.Velocity*.125f,mover.Foot),0,.00001f);
    }
    [Fact]public void AFlatObstacleReturnsItsFirstExpandedBoundaryInsteadOfATickDependentBisection()
    {
        var cells=new byte[400];cells[10*20+10]=1;var grid=new NavigationGrid(new(new(-10,-10),1,.25f,20,20,cells));
        Assert.True(grid.TryAttackObstacle(new(-5,0,.5f),new(5,0,.5f),out var fraction));Assert.Equal(.475f,fraction,6);
        Assert.True(grid.TryAttackObstacle(new(-5,0,.5f),new(-.25f,0,.5f),out fraction));Assert.Equal(1,fraction);
    }
    [Fact]public void TheNearestSurfaceObstacleWinsRegardlessOfItsStorageOrder()
    {
        var map=new SurfaceGeometry{ScenePath="res://Scenes/Regions/Test.tscn",SourceHash=new string('A',64),
            Vertices=[new(-10,0,-5),new(10,0,-5),new(10,0,5),new(-10,0,5)],Polygons=[[0,1,2,3]],
            Occluders=[new(new(3,1,0),new(1,2,1),new(0,0,0)),new(new(0,1,0),new(1,2,1),new(0,0,0))]};map.Seal();
        var visibility=new SurfaceVisibility(map);
        Assert.True(visibility.TryFirstHit(new(-4,1,0),new(5,1,0),out var fraction));Assert.Equal(3.5f/9,fraction,6);
        Assert.True(visibility.TryFirstHit(new(5,1,0),new(-4,1,0),out fraction));Assert.Equal(1.5f/9,fraction,6);
    }
}
