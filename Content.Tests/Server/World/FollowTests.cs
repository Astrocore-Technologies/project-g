using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class FollowTests
{
    private static (ServerWorld World,ServerPlayer Follower,ServerPlayer Target) Create(NavigationOptions? navigation=null,float targetHeight=0)
    {
        var world=new ServerWorld(Options.Create(new MovementOptions{MinX=-40,MaxX=40,MinZ=-20,MaxZ=20}),
            Options.Create(new InterestOptions{Radius=50,ExitRadius=54}),Options.Create(navigation??new()),catalog:ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>1);
        var initial=world.CreateInitialCharacter();
        var follower=world.AddPlayer(1,new(1),initial with{X=-8,Z=-6,Surface=world.GeometryHash==0?null:new(1,0,world.GeometryHash)});
        var target=world.AddPlayer(2,new(2),initial with{X=8,Z=-6,Surface=world.GeometryHash==0?null:new(1,targetHeight,world.GeometryHash)});
        return(world,follower,target);
    }
    private static void Step(ServerWorld world,int count){for(var i=0;i<count;i++)world.Simulate(.05f);}
    private static void Start(ServerWorld world,ServerPlayer target)=>Assert.True(world.TryStartFollow(1,new(1,0,10,target.EntityId)));

    [Fact]public void FollowMaintainsThreeMetresWithoutAttackOrInputHeartbeats()
    {
        var(w,f,t)=Create();Start(w,t);Step(w,160);
        Assert.InRange(Vector3.Distance(f.Foot,t.Foot),2.95f,3.2f);
        Assert.Equal(FollowPhase.Active,w.FollowFor(1)!.Value.Phase);
        Assert.Equal(1u,f.LastProcessedSequence);Assert.Empty(w.Combat!.Events);
        Assert.True(w.TryApplyMove(2,new(1,0,new(8,4),InputOrder:20)));Step(w,120);
        Assert.InRange(Vector3.Distance(f.Foot,t.Foot),2.95f,3.25f);
    }
    [Fact]public void ManualMoveAndActualActionCancelButRejectedSkillDoesNot()
    {
        var(w,f,t)=Create();Start(w,t);
        Assert.True(w.TryQueueAbility(1,new(1,0,ushort.MaxValue,Vector2.UnitY,InputOrder:11),0));Step(w,1);
        Assert.Equal(FollowPhase.Active,w.FollowFor(1)!.Value.Phase);
        Assert.True(w.TryApplyMove(1,new(2,1,new(-8,3),InputOrder:12)));
        Assert.Equal(FollowReason.ManualInput,w.FollowFor(1)!.Value.Reason);
        Assert.True(w.TryStartFollow(1,new(3,2,13,t.EntityId)));
        Assert.True(w.TryQueueAttack(1,new(2,0,Vector2.UnitY,InputOrder:14)));Step(w,1);
        Assert.Equal(FollowReason.Action,w.FollowFor(1)!.Value.Reason);
        Assert.Equal(f.Position,f.Target);
        Assert.False(w.TryStartFollow(1,new(1,0,10,t.EntityId)));
    }
    [Fact]public void LostLosUsesOnlyLastSeenPointAndOneFiveSecondDeadline()
    {
        var(w,f,t)=Create(new(){BlockedAreas=[new(){X=40,Z=15,Width=1,Height=10}]});
        Assert.True(t.Motion.Reset(new(-4,-6),new(-4,-6)));Start(w,t);
        Assert.True(t.Motion.Reset(new(4,0),new(4,0)));Step(w,95);
        Assert.Equal(FollowPhase.Recovering,w.FollowFor(1)!.Value.Phase);
        Assert.Equal(new Vector2(-4,-6),f.Target);
        Step(w,10);Assert.Equal(FollowPhase.Stopped,w.FollowFor(1)!.Value.Phase);
        Assert.Equal(FollowReason.LostTarget,w.FollowFor(1)!.Value.Reason);
    }
    [Fact]public void TooFarStartsRecoveryAndReturningBeforeDeadlineResumes()
    {
        var(w,f,t)=Create();Start(w,t);
        Assert.True(t.Motion.Reset(new(39,0),new(39,0)));w.Combat!.Move(t.EntityId,t.Position,0);Step(w,20);
        Assert.Equal(FollowPhase.Recovering,w.FollowFor(1)!.Value.Phase);
        Assert.True(t.Motion.Reset(new(8,-6),new(8,-6)));w.Combat!.Move(t.EntityId,t.Position,0);Step(w,1);
        Assert.Equal(FollowPhase.Active,w.FollowFor(1)!.Value.Phase);
    }
    [Fact]public void InvalidRequestPreservesRouteAndTargetRemovalStopsItsFollowers()
    {
        var(w,f,t)=Create();Start(w,t);Step(w,2);
        Assert.False(w.TryStartFollow(1,new(2,1,11,new(12345))));
        Assert.Equal(FollowPhase.Rejected,w.FollowFor(1)!.Value.Phase);
        var before=f.Position;Step(w,2);Assert.NotEqual(before,f.Position);
        w.RemovePlayer(2);Assert.Equal(FollowReason.TargetLeft,w.FollowFor(1)!.Value.Reason);
        Assert.Equal(f.Position,f.Target);
    }
    [Fact]public void RepeatedFailedPathsDoNotResetRecoveryClock()
    {
        var map=new SurfaceGeometry{Version=3,ScenePath="res://Scenes/Regions/Test.tscn",SourceHash=new string('A',64),
            Vertices=[new(-20,0,-15),new(-1,0,-15),new(-1,0,15),new(-20,0,15),new(1,0,-15),new(20,0,-15),new(20,0,15),new(1,0,15)],Polygons=[[0,1,2,3],[4,5,6,7]]};map.Seal();
        var(w,_,t)=Create(new(){ExportedSurface=map});Start(w,t);Step(w,20);
        Assert.Equal(FollowPhase.Recovering,w.FollowFor(1)!.Value.Phase);
        Step(w,90);Assert.Equal(FollowPhase.Stopped,w.FollowFor(1)!.Value.Phase);
    }
    [Fact]public void DetachedTargetImmediatelyEndsFollowEvenWhileCombatBodyRemains()
    {
        var(w,f,t)=Create();Start(w,t);w.DisconnectPvp(t.ConnectionId);
        Assert.Equal(FollowReason.TargetLeft,w.FollowFor(1)!.Value.Reason);Assert.Equal(f.Position,f.Target);
    }
    [Fact]public void RegionUnloadingIsReportedAsLeavingRatherThanDeath()
    {
        var(w,f,t)=Create();Start(w,t);t.Loaded=false;Step(w,1);
        Assert.Equal(FollowReason.TargetLeft,w.FollowFor(1)!.Value.Reason);Assert.Equal(f.Position,f.Target);
    }
    [Theory][InlineData(4)][InlineData(41)]
    public void DistancesIncludeHeightAndASeparateFloorCannotCountAsThreeMetres(float height)
    {
        var map=new SurfaceGeometry{Version=3,ScenePath="res://Scenes/Regions/Test.tscn",SourceHash=new string('A',64),
            Vertices=[new(-20,0,-15),new(20,0,-15),new(20,0,15),new(-20,0,15),new(-20,height,-15),new(20,height,-15),new(20,height,15),new(-20,height,15)],Polygons=[[0,1,2,3],[4,5,6,7]]};map.Seal();
        var(w,f,t)=Create(new(){ExportedSurface=map},height);
        Assert.True(t.Motion.Reset(f.Position,f.Position,height,height));w.Combat!.Move(t.EntityId,t.Position,height);
        var accepted=w.TryStartFollow(1,new(1,0,10,t.EntityId));
        if(height>40){Assert.False(accepted);Assert.Equal(FollowReason.TooFar,w.FollowFor(1)!.Value.Reason);}
        else{Assert.True(accepted);Step(w,20);Assert.Equal(FollowPhase.Recovering,w.FollowFor(1)!.Value.Phase);}
    }
    [Fact]public void DamageAloneKeepsFollowButDeathEndsIt()
    {
        var(w,f,t)=Create();Start(w,t);w.Combat!.DamagePermission=(_,_)=>true;
        Assert.True(w.Combat.ApplyAbilityDamage(f.EntityId,1,t.EntityId)>0);Step(w,1);
        Assert.Equal(FollowPhase.Active,w.FollowFor(1)!.Value.Phase);
        Assert.True(w.Combat.ApplyAbilityDamage(t.EntityId,100000,f.EntityId)>0);Step(w,1);
        Assert.Equal(FollowReason.Died,w.FollowFor(1)!.Value.Reason);
    }
}
