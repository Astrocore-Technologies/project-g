using System.Numerics;
using Content.Server.Combat;
using Content.Shared.Combat;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class Basic3DAimTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void ExplicitDownwardAimReachesTheProneBodyWhileAFlatAimMisses(bool down)
    {
        var f=new BasicActionPhaseTests.Fight();f.Combat.Move(f.Target,new(0,1.5f));f.Spatial.Move(f.Target,new(0,1.5f));
        Assert.True(f.Combat.ApplyStatusControl(default,f.Target,0,0,CrowdControlKind.Knockdown,2));
        var direction=Vector3.Normalize(new(0,down?-.6f:0,1.5f));
        Assert.True(f.Combat.Queue(f.Player,new(1,0,new(direction.X,direction.Z),DirectionY:direction.Y),0));f.Step();
        Assert.True(f.Combat.TryBasicState(f.Player,f.Tick,out var state));Assert.Equal(direction.Y,state.DirectionY,6);
        Assert.Equal(1,state.OriginHeightOffset);Assert.Equal(.3f,state.Thickness);
        f.Step(.25f);var contact=Assert.Single(f.Combat.Events);Assert.Equal(down,contact.TargetId.IsValid);Assert.Equal(direction.Y,contact.DirectionY,6);
    }

    [Fact]public void AnAcceptedPreparationAimChangesPitchButActiveCannotRetargetIt()
    {
        var f=new BasicActionPhaseTests.Fight();f.Begin();var id=f.Actor.Action!.ExecutionId;
        Assert.True(f.Combat.UpdateBasicAim(f.Player,1,id,new(0,.8f),-.6f));f.Step(.25f);
        Assert.Equal(-.6f,Assert.Single(f.Combat.Events).DirectionY,6);
        Assert.False(f.Combat.UpdateBasicAim(f.Player,1,id,Vector2.Zero,1));
    }

    [Theory][InlineData(1f)][InlineData(-1f)]
    public void StrictlyVerticalAimRetainsTheLastServerYaw(float y)
    {
        var f=new BasicActionPhaseTests.Fight();Assert.True(f.Combat.FaceIntent(f.Player,Vector2.UnitX));
        Assert.True(f.Combat.Queue(f.Player,new(1,0,Vector2.Zero,DirectionY:y),0));f.Step();
        Assert.Equal(Vector2.UnitX,f.Actor.Facing);Assert.True(f.Combat.TryBasicState(f.Player,f.Tick,out var pose));
        Assert.Equal(Vector2.Zero,pose.Direction);Assert.Equal(y,pose.DirectionY);f.Step(.25f);
        Assert.Equal(Vector2.UnitX,f.Actor.Facing);
    }
}
