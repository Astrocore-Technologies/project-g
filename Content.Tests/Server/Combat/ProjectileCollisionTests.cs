using System.Numerics;
using Content.Server.Combat;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ProjectileCollisionTests
{
    [Fact]public void LinearContactUsesRelativeVelocityAndIncludesItsEndpoint()
    {
        Assert.True(ProjectileCollision.TryTime(new(-1.5f,0,0),new(4,0,0),Vector3.Zero,.5,.25,out var time));
        Assert.Equal(.25,time,12);
        Assert.False(ProjectileCollision.TryTime(new(-1.5f,0,0),Vector3.Zero,Vector3.Zero,.5,10,out _));
        Assert.False(ProjectileCollision.TryTime(new(-1.5f,0,0),new(-4,0,0),Vector3.Zero,.5,10,out _));
    }
    [Fact]public void AQuadraticCrossingIsFoundEvenWhenBothEndpointsAreOutside()
    {
        Assert.True(ProjectileCollision.TryTime(new(0,2,0),new(0,-4,0),new(0,4,0),.5,2,out var time));
        Assert.Equal(.5,time,10);
    }
    [Fact]public void AQuadraticTangentIsAContactAndANearMissRemainsAMiss()
    {
        Assert.True(ProjectileCollision.TryTime(new(0,2,0),new(0,-2,0),new(0,2,0),1,2,out var time));
        Assert.Equal(1,time,10);
        Assert.False(ProjectileCollision.TryTime(new(0,2.00001f,0),new(0,-2,0),new(0,2,0),1,2,out _));
    }
    [Fact]public void InitialOverlapDoesNotRequireAnExtraServerStep()
    {
        Assert.True(ProjectileCollision.TryTime(new(.25f,0,0),new(4,0,0),Vector3.Zero,.5,0,out var time));Assert.Equal(0,time);
    }
}
