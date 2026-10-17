using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class NavigationCancellationTests
{
    private static (ServerWorld World,ServerPlayer Player) Create(ContentCatalog? catalog=null)
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:catalog??ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>1);
        return (world,world.AddPlayer(42,new(1)));
    }

    [Fact]
    public void AcceptedAttackStopsOldRouteAndRejectsItsDelayedHeartbeats()
    {
        var (world,player)=Create();
        var target=player.Position+Vector2.UnitY*5;
        Assert.True(world.TryApplyMove(42,new(1,0,target,InputOrder:10)));
        world.Simulate(.05f);
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitY,InputOrder:11)));
        world.Simulate(.05f);
        Assert.Equal(11u,player.CancelledNavigationOrder);
        var stopped=player.Position;
        Assert.Equal(stopped,player.Target);
        Assert.False(world.TryApplyMove(42,new(2,1,target,InputOrder:10)));
        for(var i=0;i<30;i++)world.Simulate(.05f);
        Assert.Equal(stopped,player.Position);
    }

    [Fact]
    public void ActualActionStartCancelsTheCurrentRouteEvenIfItsInputArrivedEarlier()
    {
        var (world,player)=Create();var target=player.Position+Vector2.UnitY*4;
        Assert.True(world.TryApplyMove(42,new(1,0,target,InputOrder:12)));
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitX,InputOrder:11)));
        world.Simulate(.05f);
        Assert.Equal(12u,player.CancelledNavigationOrder);
        Assert.Equal(player.Position,player.Target);
        Assert.False(world.TryApplyMove(42,new(2,1,target,InputOrder:12)));
    }

    [Fact]
    public void RejectedSkillDoesNotCancelMovement()
    {
        var (world,player)=Create();var target=player.Position+Vector2.UnitY*5;
        Assert.True(world.TryApplyMove(42,new(1,0,target,InputOrder:10)));
        world.TryQueueAbility(42,new(1,0,ushort.MaxValue,Vector2.UnitY,InputOrder:11),0);
        world.Simulate(.05f);
        Assert.Equal(0u,player.CancelledNavigationOrder);
        Assert.Equal(target,player.Target);
    }

    [Fact]
    public void OnlyFirstBlockCancelsRouteAndHeartbeatsAllowNewBackwardsMovement()
    {
        var (world,player)=Create();world.Combat!.Get(player.EntityId).ProfessionAllowsUnarmedBlock=true;
        Assert.True(world.TryApplyMove(42,new(1,0,player.Position+Vector2.UnitY*4,InputOrder:10)));
        Assert.True(world.TryQueueDefense(42,new(1,DefenseAction.Block,Vector2.UnitY,InputOrder:11)));
        world.Simulate(.05f);
        Assert.Equal(11u,player.CancelledNavigationOrder);
        Assert.True(world.TryApplyMove(42,new(2,1,player.Position-Vector2.UnitY*3,InputOrder:12)));
        var before=player.Position;
        Assert.True(world.TryQueueDefense(42,new(2,DefenseAction.Block,Vector2.UnitX,InputOrder:13)));
        world.Simulate(.05f);
        Assert.Equal(11u,player.CancelledNavigationOrder);
        Assert.True(player.Position.Y<before.Y);
    }

    [Fact]
    public void DeniedMovementIsNotQueuedUntilRootEnds()
    {
        var (world,player)=Create();
        world.Combat!.Stun(player.EntityId,.15);
        var target=player.Position+Vector2.UnitY*3;
        Assert.False(world.TryApplyMove(42,new(1,0,target,InputOrder:10)));
        Assert.Equal(10u,player.CancelledNavigationOrder);
        world.Simulate(.25f);
        Assert.False(world.TryApplyMove(42,new(2,1,target,InputOrder:10)));
        Assert.True(world.TryApplyMove(42,new(3,2,target,InputOrder:11)));
    }

    [Fact]
    public void NewMovementCancelsContinuousInputAndLateHeartbeatCannotRestartIt()
    {
        var (world,player)=Create();
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitY,BasicAttackMode.BeginContinuous,InputOrder:10)));
        world.Simulate(.05f);
        Assert.True(world.Combat!.HasContinuousBasic(player.EntityId));
        var target=player.Position+Vector2.UnitY*3;
        Assert.True(world.TryApplyMove(42,new(1,1,target,InputOrder:11)));
        Assert.False(world.Combat.HasContinuousBasic(player.EntityId));
        world.Simulate(.05f);
        Assert.False(world.TryQueueAttack(42,new(2,1,Vector2.UnitY,BasicAttackMode.UpdateContinuous,InputOrder:10)));
        Assert.Equal(target,player.Target);
    }

    [Fact]
    public void MovementCancelsAnOlderContinuousPressBeforeItStarts()
    {
        var (world,player)=Create();
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitY,BasicAttackMode.BeginContinuous,InputOrder:10)));
        var target=player.Position+Vector2.UnitY*3;
        Assert.True(world.TryApplyMove(42,new(1,0,target,InputOrder:11)));
        world.Simulate(.05f);
        Assert.Equal(0u,player.CancelledNavigationOrder);
        Assert.False(world.Combat!.HasContinuousBasic(player.EntityId));
        Assert.Equal(target,player.Target);
    }

    [Fact]
    public void BufferedAttackKeepsRouteUntilItActuallyStarts()
    {
        var (world,player)=Create();var target=player.Position+Vector2.UnitY*4;
        Assert.True(world.TryApplyMove(42,new(1,0,target,InputOrder:10)));
        world.Combat!.SetRecovery(player.EntityId,.12);
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitX,InputOrder:11)));
        world.Simulate(.05f);
        Assert.Equal(AttackOutcome.Buffered,world.Combat.Results[player.EntityId].Outcome);
        Assert.Equal(0u,player.CancelledNavigationOrder);
        Assert.Equal(target,player.Target);
        Assert.True(world.TryApplyMove(42,new(2,1,target+Vector2.UnitY,InputOrder:12)));
        world.Simulate(.1f);
        Assert.Equal(12u,player.CancelledNavigationOrder);
        Assert.Equal(player.Position,player.Target);
    }

    [Fact]
    public void StationarySkillRejectsNewPointWithoutSavingItForTheEndOfTheCast()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["abilities"]![0]!["action"]!["preparation"]!["movementMultiplier"]=0;
        var (world,player)=Create(ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
        Assert.True(world.TryQueueAbility(42,new(1,0,1,Vector2.UnitY,InputOrder:11),0));
        world.Simulate(.05f);
        Assert.True(world.Combat!.Get(player.EntityId).StationaryCast);
        var target=player.Position+Vector2.UnitY*4;
        Assert.False(world.TryApplyMove(42,new(1,1,target,InputOrder:12)));
        var stopped=player.Position;
        for(var i=0;i<40;i++)world.Simulate(.05f);
        Assert.False(world.TryApplyMove(42,new(2,2,target,InputOrder:12)));
        Assert.Equal(stopped,player.Position);
        Assert.True(world.TryApplyMove(42,new(3,3,target,InputOrder:13)));
    }

    [Fact]
    public void RejectedManualInputCannotStopForcedFearMovement()
    {
        var (world,player)=Create();var combat=world.Combat!;
        combat.DamagePermission=(_,_)=>true;
        Assert.True(combat.ApplyStatusControl(world.TrainingTargetId,player.EntityId,1,1,
            Content.Shared.Combat.CrowdControlKind.Fear,1));
        world.Simulate(.05f);
        Assert.True(combat.IsFleeing(player.EntityId));
        Assert.True(player.Motion.IsMoving);
        var forcedTarget=player.Target;
        Assert.False(world.TryApplyMove(42,new(1,1,player.Position+Vector2.UnitY*3,InputOrder:11)));
        Assert.Equal(forcedTarget,player.Target);
        Assert.True(player.Motion.IsMoving);
    }

    [Fact]
    public void CancellationComparisonHandlesInputOrderWraparound()
    {
        var (world,player)=Create();var target=player.Position+Vector2.UnitY*4;
        Assert.True(world.TryApplyMove(42,new(1,0,target,InputOrder:uint.MaxValue-1)));
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitY,InputOrder:uint.MaxValue)));
        world.Simulate(.05f);
        Assert.False(world.TryApplyMove(42,new(2,1,target,InputOrder:uint.MaxValue-1)));
        Assert.True(world.TryApplyMove(42,new(3,2,target,InputOrder:1)));
    }
}
