using System.Numerics;
using Content.Server.Combat;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class MeleeTrajectoryTests
{
    private static CombatCapsule Body(float x,float y,float z,float radius=.4f)=>new(new(x,y,z),Vector3.UnitY,.6,radius);
    [Fact]public void ArcSelectsTheFirstBladeAngleRatherThanTheNearestEnemy()
    {
        var arc=new MeleeArc(Vector3.UnitY,Vector3.UnitZ,Vector2.UnitY,3,Math.PI*.5,.3);
        Assert.True(arc.TryFirstContact(Body(-1,1,2),out var left,out _));
        Assert.True(arc.TryFirstContact(Body(.5f,1,1),out var nearerRight,out _));Assert.True(left<nearerRight);
        var expected=(Math.Atan2(-1,2)-Math.Asin(.55/Math.Sqrt(5))+Math.PI/4)/(Math.PI/2);
        Assert.Equal(expected,left,6);
    }
    [Fact]public void ThrustSelectsTheFirstSweptTipContactIncludingACloserLargeBodySurface()
    {
        var thrust=new MeleeThrust(Vector3.UnitY,Vector3.UnitZ,3,.3);
        Assert.True(thrust.TryFirstContact(Body(0,1,2,.8f),out var wide,out _));
        Assert.True(thrust.TryFirstContact(Body(0,1,1.8f,.2f),out var thin,out _));
        Assert.True(wide<thin);Assert.Equal((2-1.1)/2.7,wide,6);
        Assert.False(thrust.TryFirstContact(Body(0,1,3.41f),out _,out _));
        Assert.True(thrust.TryFirstContact(Body(0,1,3.39f),out _,out _));
    }
    [Fact]public void ArcFirstContactAgreesWithTheIndependentWholeVolumeSolverAcrossArbitraryCapsules()
    {
        var random=new Random(35189);
        for(var i=0;i<600;i++)
        {
            float Next(float low,float high)=>low+(float)random.NextDouble()*(high-low);
            var direction=Vector3.Normalize(new Vector3(Next(-1,1),Next(-1,1),Next(-1,1)));
            var arc=new MeleeArc(new(1,2,3),direction,Vector2.UnitY,3,Next(.1f,MathF.Tau),.3);
            var body=new CombatCapsule(new(Next(-2,4),Next(-1,5),Next(0,6)),
                Vector3.Normalize(new(Next(-1,1),Next(-1,1),Next(-1,1))),Next(0,2),Next(.05f,.8f));
            var intersects=arc.Intersects(body);var actual=arc.TryFirstContact(body,out var first,out var witness);
            Assert.Equal(intersects,actual);
            if(!actual)continue;
            Assert.InRange(first,0,1);Assert.True(body.IntersectsSphere(witness,.150002),$"Witness outside body at sample {i}");
        }
    }
    [Theory][InlineData(-1f)][InlineData(1f)]
    public void VerticalThrustAndTiltedArcUseTheirFullThreeDimensionalPaths(float y)
    {
        var body=Body(0,1+y*2,0);
        var thrust=new MeleeThrust(Vector3.UnitY,new(0,y,0),3,.3);Assert.True(thrust.TryFirstContact(body,out var at,out _));Assert.InRange(at,0,1);
        var arc=new MeleeArc(Vector3.UnitY,new(0,y,0),Vector2.UnitY,3,Math.PI/2,.3);Assert.True(arc.TryFirstContact(body,out at,out _));Assert.InRange(at,0,1);
    }
}
