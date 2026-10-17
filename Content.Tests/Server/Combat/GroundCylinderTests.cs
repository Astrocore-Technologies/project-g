using System.Numerics;
using Content.Server.Combat;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class GroundCylinderTests
{
    [Theory][InlineData(2.35f,1,true)][InlineData(2.41f,1,false)][InlineData(0,3,true)][InlineData(0,3.001f,false)]
    [InlineData(0,-1,true)][InlineData(0,-1.001f,false)]
    public void BodyEdgesAndTopBottomCapsUseTheClosedCylinderInsteadOfTheirFootOrCenter(float x,float y,bool expected)
    {
        var cylinder=new GroundCylinder(Vector3.Zero,2,2);var body=new CombatCapsule(new(x,y,0),Vector3.UnitY,.6,.4);
        Assert.Equal(expected,cylinder.TryContact(body,out _));
    }
    [Theory][InlineData(.28f,true)][InlineData(.3f,false)]
    public void TheCapsuleRadiusDoesNotTurnTheTopRimIntoAnExpandedBox(float offset,bool expected)
    {
        var cylinder=new GroundCylinder(Vector3.Zero,2,2);var body=new CombatCapsule(new(2+offset,2+offset,0),Vector3.UnitY,0,.4);
        Assert.Equal(expected,cylinder.TryContact(body,out _));
    }
    [Fact]public void AProneBodyCanOverlapWhileItsCenterLiesBeyondTheArea()
    {
        var cylinder=new GroundCylinder(new(100,5,-10),2,2);
        var body=new CombatCapsule(new(100,5.4f,-13),Vector3.UnitZ,1.2,.4);
        Assert.True(cylinder.TryContact(body,out var witness));Assert.True(body.IntersectsSphere(witness,.00001));
        Assert.InRange(witness.Y,5,7);Assert.InRange(Vector2.Distance(new(witness.X,witness.Z),new(100,-10)),0,2.00001f);
    }
    [Fact]public void TiltedCapsulesHaveValidWitnessesAndDoNotMissIndependentlySampledIntersections()
    {
        var random=new Random(67134);var cylinder=new GroundCylinder(Vector3.Zero,2,2);
        for(var sample=0;sample<120;sample++)
        {
            float Next(float low,float high)=>low+(float)random.NextDouble()*(high-low);
            var body=new CombatCapsule(new(Next(-3,3),Next(-1,3),Next(-3,3)),Vector3.Normalize(new(Next(-1,1),Next(-1,1),Next(-1,1))),1,.4);
            var actual=cylinder.TryContact(body,out var witness);
            if(actual)
            {
                Assert.True(body.IntersectsSphere(witness,.000002));Assert.InRange(witness.Y,-.000001f,2.000001f);
                Assert.True((double)witness.X*witness.X+(double)witness.Z*witness.Z<=4.00001);
            }
            for(var step=0;step<=512;step++)
            {
                var point=body.Center+body.Axis*(float)(-body.HalfSegment+2*body.HalfSegment*step/512);
                var radial=Math.Max(0,Math.Sqrt((double)point.X*point.X+(double)point.Z*point.Z)-2);
                var vertical=point.Y-Math.Clamp(point.Y,0,2);
                if(radial*radial+vertical*vertical<.16-1e-6){Assert.True(actual);break;}
            }
        }
    }
}
