using System.Numerics;
using Content.Server.Combat;
using Content.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class MeleeArcTests
{
    private static MeleeArc Arc(Vector3? direction=null,double range=2,double thickness=.3,double degrees=90)
        =>new(new(0,1,0),direction??Vector3.UnitZ,Vector2.UnitY,range,degrees*Math.PI/180,thickness);
    private static CombatCapsule Body(float x,float z,float footY=0,bool prone=false)
        =>CombatCapsule.Create(new HurtboxDefinition{Shape=HurtboxShape.Capsule,Height=2,Radius=.4f},new(x,footY,z),Vector2.UnitY,prone);

    [Fact]public void BodyEdgeCanIntersectBeyondRangeWhileTheNextBodyMisses()
    {
        var arc=Arc();Assert.True(arc.Intersects(Body(0,2.39f)));Assert.False(arc.Intersects(Body(0,2.41f)));
        Assert.False(arc.Intersects(Body(0,-1)));Assert.True(arc.Intersects(Body(.5f,1)));
    }

    [Fact]public void ThicknessIsIncludedInTheOuterExtentRatherThanAddedBeyondRange()
    {
        var arc=Arc(thickness:.5);var body=new CombatCapsule(new(0,1,2.25f),Vector3.UnitY,.5,.25);
        Assert.True(arc.Intersects(body));Assert.False(arc.Intersects(body with {Center=new(0,1,2.2501f)}));
        Assert.Equal(.25,arc.DistanceSquared(body),12);
    }

    [Fact]public void TheArcSidesAreSweptBladeEdgesWithRealThickness()
    {
        var arc=Arc();
        CombatCapsule At(double angle)=>new(new((float)(1.5*Math.Sin(angle*Math.PI/180)),1,(float)(1.5*Math.Cos(angle*Math.PI/180))),Vector3.UnitY,0,.1);
        Assert.True(arc.Intersects(At(50)));Assert.False(arc.Intersects(At(60)));
        Assert.True(arc.Intersects(At(-50)));Assert.False(arc.Intersects(At(-60)));
    }

    [Fact]public void ManualPitchCanReachAnAirborneBodyThatAFlatSwingMisses()
    {
        var target=Body(0,1.5f,2);
        Assert.False(Arc(range:3).Intersects(target));
        Assert.True(Arc(new(0,2,1.5f),range:3).Intersects(target));
    }

    [Fact]public void AProneBodyRequiresTheVolumeToActuallyReachItsHeight()
    {
        var target=Body(0,1.5f,prone:true);
        Assert.False(Arc().Intersects(target));Assert.True(Arc(new(0,-.6f,1.5f)).Intersects(target));
    }

    [Fact]public void ALongTargetSegmentCanCrossTheVolumeWithBothEndsOutside()
    {
        var target=new CombatCapsule(new(0,1,1),Vector3.UnitX,3,.1);
        var arc=Arc();Assert.True(arc.Intersects(target));Assert.Equal(0,arc.DistanceSquared(target),12);
        Assert.False(arc.Intersects(target with {Center=new(0,1.251f,1)}));
    }

    [Fact]public void RoundedOuterArcIsNotReplacedWithItsBoundingRectangle()
    {
        var arc=Arc(thickness:.5);var target=new CombatCapsule(new(1.5f,1,1.5f),Vector3.UnitY,0,.1);
        Assert.False(arc.Intersects(target));
        Assert.True(arc.Intersects(target with {Center=new(1.4f,1,1.4f)}));
    }

    [Theory][InlineData(90,false)][InlineData(180,false)][InlineData(270,false)][InlineData(360,true)]
    public void LargerAuthoredSweepsHaveTheExpectedRearCoverage(double degrees,bool rear)
    {
        var target=new CombatCapsule(new(0,1,-1),Vector3.UnitY,0,.1);
        Assert.Equal(rear,Arc(degrees:degrees).Intersects(target));
    }

    [Fact]public void AStrictlyVerticalAimUsesTheRetainedYawToOrientItsPlane()
    {
        var arc=Arc(Vector3.UnitY);Assert.True(arc.Intersects(Body(.75f,0,1)));
        Assert.False(arc.Intersects(Body(0,1,1)));
    }

    [Fact]public void DistanceAgreesWithIndependentSamplesOfTheEntireSweptBlade()
    {
        var random=new Random(8301);var arc=Arc();
        for(var sample=0;sample<64;sample++)
        {
            var center=new Vector3(random.NextSingle()*6-3,random.NextSingle()*5-1,random.NextSingle()*6-3);
            var axis=Vector3.Normalize(new Vector3(random.NextSingle()-.5f,random.NextSingle()-.5f,random.NextSingle()-.5f));
            var body=new CombatCapsule(center,axis,.6,.4);var sampled=double.PositiveInfinity;
            var a=center-axis*.6f;var b=center+axis*.6f;
            for(var angle=0;angle<=64;angle++)for(var radial=0;radial<=24;radial++)
            {
                var theta=-Math.PI/4+Math.PI/2*angle/64;var radius=1.85*radial/24;
                var point=new Vector3((float)(radius*Math.Sin(theta)),1,(float)(radius*Math.Cos(theta)));
                var t=Math.Clamp(Vector3.Dot(point-a,b-a)/Vector3.DistanceSquared(a,b),0,1);
                sampled=Math.Min(sampled,Vector3.DistanceSquared(point,Vector3.Lerp(a,b,t)));
            }
            var exact=arc.DistanceSquared(body);
            arc.TryContact(body,out var blade);
            var witness=body.DistanceSquared((double)blade.X-center.X,(double)blade.Y-center.Y,(double)blade.Z-center.Z);
            Assert.InRange(Math.Abs(witness-exact),0,.000004);
            Assert.True(Vector3.Distance(new(0,1,0),blade)<=1.850001f);
            Assert.True(exact<=sampled+.000003,$"Exact {exact} exceeds sampled {sampled} at sample {sample}.");
            Assert.InRange(Math.Sqrt(sampled)-Math.Sqrt(exact),-.00001,.08);
        }
    }
}
