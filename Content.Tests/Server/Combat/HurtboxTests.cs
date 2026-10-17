using System.Numerics;
using System.Text;
using Content.Server.Combat;
using Content.Server.Data;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class HurtboxTests
{
    [Fact]public void ShippedBodiesHaveIndividualExplicitProfiles()
    {
        var catalog=ContentCatalogTests.Load();
        foreach(var id in new[]{"test_adventurer","test_creature","arena_dummy"})
        {Assert.Equal(2,catalog.Creatures[id].Hurtbox.Height);Assert.Equal(.4f,catalog.Creatures[id].Hurtbox.Radius);}
        Assert.Equal(.7f,catalog.Creatures["test_boss"].Hurtbox.Radius);
        Assert.All(catalog.Abilities.Values.Where(a=>a.Kind==AbilityKind.Projectile),a=>Assert.Equal(1,a.ProjectileLaunchHeight));
    }

    [Theory][InlineData("missing")][InlineData("null")][InlineData("shape")][InlineData("radius")][InlineData("height")][InlineData("short")]
    public void InvalidBodyFailsBeforeSimulation(string field)
    {
        var data=BasicActionPhaseTests.Document();var creature=data["creatures"]![0]!.AsObject();
        var body=creature["hurtbox"]!;
        switch(field)
        {
            case "missing":creature.Remove("hurtbox");break;
            case "null":creature["hurtbox"]=null;break;
            case "shape":body["shape"]="Sphere";break;
            case "radius":body["radius"]=-.1;break;
            case "height":body["height"]=0;break;
            case "short":body["height"]=.5;break;
        }
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }

    [Fact]public void PoseUsesFeetSupportAirOffsetAndFacingRatherThanAnimatedBones()
    {
        var f=new BasicActionPhaseTests.Fight();var actor=f.Actor;
        actor.Height=4;actor.AirOffset=1.5f;
        var body=actor.Hurtbox;Assert.Equal(new Vector3(0,6.5f,0),body.Center);Assert.Equal(Vector3.UnitY,body.Axis);
        Assert.Equal(.6,body.HalfSegment,6);
        actor.AirOffset=0;actor.Control=CombatControlPhase.KnockedDown;actor.Facing=Vector2.UnitX;
        body=actor.Hurtbox;Assert.Equal(new Vector3(0,4.4f,0),body.Center);Assert.Equal(Vector3.UnitX,body.Axis);
        Assert.True(body.IntersectsSphere(new(1,4.4f,0),0));
        Assert.False(body.IntersectsSphere(new(0,5,0),.1));
    }

    [Theory][InlineData(.4,2.5)][InlineData(.7,2.2)]
    public void ProjectileContactsTheActualBodySurface(double radius,double distance)
    {
        var body=new CombatCapsule(Vector3.Zero,Vector3.UnitY,.5,radius);
        Assert.True(ProjectileCollision.TryCapsuleTime(-3,0,0,Vector3.UnitX,Vector3.Zero,body,.1,4,out var at));
        Assert.Equal(distance,at,10);
    }

    [Fact]public void CapsuleSideCanBeHitWhileBothEndSpheresAreMissed()
    {
        var body=new CombatCapsule(Vector3.Zero,Vector3.UnitY,1,.25);
        Assert.True(ProjectileCollision.TryCapsuleTime(-2,0,0,new(4,0,0),Vector3.Zero,body,.25,1,out var at));
        Assert.Equal(.375,at,12);
        Assert.False(ProjectileCollision.TryTime(-2,1,0,new(4,0,0),Vector3.Zero,.5,1,out _));
    }

    [Fact]public void EndCapsAreRoundedInsteadOfAnExpandedBoundingBox()
    {
        var body=new CombatCapsule(Vector3.Zero,Vector3.UnitY,1,.25);
        Assert.True(ProjectileCollision.TryCapsuleTime(-2,1.4,0,new(4,0,0),Vector3.Zero,body,.25,1,out var at));
        Assert.Equal((2-Math.Sqrt(.25-.16))/4,at,10);
        Assert.False(ProjectileCollision.TryCapsuleTime(-2,1.4,.4,new(4,0,0),Vector3.Zero,body,.25,1,out _));
    }

    [Fact]public void AProneCapsuleChangesBothHeightAndHorizontalReach()
    {
        var body=new CombatCapsule(new(0,.4f,0),Vector3.UnitX,.6,.4);
        Assert.True(ProjectileCollision.TryCapsuleTime(-2,0,0,Vector3.UnitX,Vector3.Zero,body,.1,3,out var at));
        Assert.Equal(.9,at,10);
        Assert.False(ProjectileCollision.TryCapsuleTime(-2,.6,0,Vector3.UnitX,Vector3.Zero,body,.1,3,out _));
    }

    [Fact]public void QuadraticSideContactsIncludeTangencyAndLaterValidCylinderRoots()
    {
        var body=new CombatCapsule(Vector3.Zero,Vector3.UnitY,1,.5);
        Assert.True(ProjectileCollision.TryCapsuleTime(1.5,0,0,new(-2,0,0),new(2,0,0),body,0,2,out var at));
        Assert.Equal(1,at,10);
        Assert.False(ProjectileCollision.TryCapsuleTime(1.50001,0,0,new(-2,0,0),new(2,0,0),body,0,2,out _));
        Assert.True(ProjectileCollision.TryCapsuleTime(2,3,0,new(-4,-1,0),new(2,0,0),body,0,4,out at));
        Assert.Equal(2+Math.Sqrt(1.5),at,10);
    }

    [Fact]public void InitialOverlapAndExactEndpointDoNotNeedAnotherTick()
    {
        var body=new CombatCapsule(Vector3.Zero,Vector3.UnitY,1,.25);
        Assert.True(ProjectileCollision.TryCapsuleTime(.25,0,0,Vector3.UnitX,Vector3.Zero,body,.25,0,out var at));Assert.Equal(0,at);
        Assert.True(ProjectileCollision.TryCapsuleTime(-1.5,0,0,Vector3.UnitX,Vector3.Zero,body,.25,1,out at));Assert.Equal(1,at);
    }

    [Theory][InlineData("missing")][InlineData("null")][InlineData("negative")][InlineData("overflow")][InlineData("otherKind")]
    public void LaunchHeightIsAnExplicitValidatedProjectileProperty(string scenario)
    {
        var data=BasicActionPhaseTests.Document();var skill=data["abilities"]![0]!.AsObject();
        switch(scenario)
        {
            case "missing":skill.Remove("projectileLaunchHeight");break;
            case "null":skill["projectileLaunchHeight"]=null;break;
            case "negative":skill["projectileLaunchHeight"]=-1;break;
            case "overflow":skill["projectileLaunchHeight"]=1e100;break;
            case "otherKind":data["abilities"]![1]!["projectileLaunchHeight"]=1;break;
        }
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }

    [Fact]public void QuadraticSweepsAgreeWithIndependentClosestSegmentDistances()
    {
        var random=new Random(8231);
        float Next()=>random.NextSingle()*8-4;
        for(var sample=0;sample<200;sample++)
        {
            var axis=sample%2==0?Vector3.UnitY:Vector3.Normalize(new Vector3(Next(),0,Next()));
            var body=new CombatCapsule(Vector3.Zero,axis,.75,.25);
            var start=new Vector3(Next(),Next(),Next());var speed=new Vector3(Next(),Next(),Next());var change=new Vector3(Next(),Next(),Next());
            var hit=ProjectileCollision.TryCapsuleTime(start.X,start.Y,start.Z,speed,change,body,.125,2,out var at);
            double Distance(double t)
            {
                var point=start+speed*(float)t+change*(float)(.5*t*t);
                var a=-axis*.75f;var b=axis*.75f;
                var along=Math.Clamp(Vector3.Dot(point-a,b-a)/Vector3.DistanceSquared(a,b),0,1);
                return Vector3.DistanceSquared(point,Vector3.Lerp(a,b,along));
            }
            if(hit){Assert.InRange(at,0,2);Assert.True(Distance(at)<=.375*.375+.000002);}
            for(var i=0;i<=128;i++)if(Distance(i/64d)<.375*.375-.000002)Assert.True(hit);
        }
    }
}
