using System.Numerics;
using Content.Server.Combat;
using Content.Shared.Combat;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class BasicControlTests
{
    [Fact]public void ManualAimIsQueuedValidatedAndAppliedOnlyToItsOwnedPreparation()
    {
        var f=new BasicActionPhaseTests.Fight();f.Begin();var execution=f.Actor.Action!.ExecutionId;
        Assert.False(f.Combat.QueueBasicControl(f.Target,new(1,1,execution,BasicControlKind.Cancel,Vector2.UnitY),f.Tick));
        Assert.False(f.Combat.QueueBasicControl(new(999),new(1,1,execution,BasicControlKind.Aim,Vector2.UnitX),f.Tick));
        Assert.True(f.Combat.QueueBasicControl(f.Player,new(1,1,execution+1,BasicControlKind.Aim,Vector2.UnitX),f.Tick));f.Step();
        Assert.True(f.Combat.TryBasicState(f.Player,f.Tick,out var unchanged));Assert.Equal(Vector2.UnitY,unchanged.Direction);
        Assert.True(f.Combat.QueueBasicControl(f.Player,new(2,1,execution,BasicControlKind.Aim,Vector2.UnitX),f.Tick));
        Assert.False(f.Combat.QueueBasicControl(f.Player,new(2,1,execution,BasicControlKind.Aim,Vector2.UnitY),f.Tick));
        Assert.True(f.Combat.TryBasicState(f.Player,f.Tick,out unchanged));Assert.Equal(Vector2.UnitY,unchanged.Direction);
        f.Step();Assert.True(f.Combat.TryBasicState(f.Player,f.Tick,out var turned));Assert.Equal(Vector2.UnitX,turned.Direction);
        f.Step(.11f);var hit=Assert.Single(f.Combat.Events);Assert.Equal(Vector2.UnitX,hit.Direction);Assert.False(hit.TargetId.IsValid);
        Assert.True(f.Combat.QueueBasicControl(f.Player,new(3,1,execution,BasicControlKind.Aim,Vector2.UnitY),f.Tick));f.Step();
        Assert.True(f.Combat.TryBasicState(f.Player,f.Tick,out var active));Assert.Equal(Vector2.UnitX,active.Direction);
    }

    [Fact]public void ExplicitCancelCannotErasePendingInputButPreAckCancelCan()
    {
        var f=new BasicActionPhaseTests.Fight();
        Assert.True(f.Combat.Queue(f.Player,new(1,0,Vector2.UnitY),f.Tick));
        Assert.True(f.Combat.QueueBasicControl(f.Player,new(1,1,ulong.MaxValue,BasicControlKind.Cancel,Vector2.UnitY),f.Tick));f.Step();
        Assert.NotNull(f.Actor.Action);Assert.Equal(AttackOutcome.Accepted,f.Combat.Results[f.Player].Outcome);
        Assert.True(f.Combat.StopBasic(f.Player,ActionStopReason.Voluntary));
        Assert.True(f.Combat.Queue(f.Player,new(2,0,Vector2.UnitY),f.Tick));
        Assert.True(f.Combat.QueueBasicControl(f.Player,new(2,2,0,BasicControlKind.Cancel,Vector2.UnitY),f.Tick));f.Step();
        Assert.Equal(AttackOutcome.Cancelled,f.Combat.Results[f.Player].Outcome);Assert.Null(f.Actor.Action);
        f.Step(2);Assert.Empty(f.Combat.Events);
    }

    [Fact]public void CancelUsesPhasePolicyAndNeverShortensTheNextBasicInterval()
    {
        var f=new BasicActionPhaseTests.Fight();f.Begin();var action=f.Actor.Action!;var ready=f.Actor.ReadyAt;f.Step(.21f);
        Assert.True(f.Combat.QueueBasicControl(f.Player,new(1,1,action.ExecutionId,BasicControlKind.Cancel,Vector2.UnitY),f.Tick));f.Step();
        Assert.Equal(AttackOutcome.CancelDenied,f.Combat.Results[f.Player].Outcome);Assert.Same(action,f.Actor.Action);
        f.Step(.1f);Assert.Equal(CombatActionPhase.Recovery,action.Phase);
        Assert.True(f.Combat.QueueBasicControl(f.Player,new(2,1,action.ExecutionId,BasicControlKind.Cancel,Vector2.UnitY),f.Tick));f.Step();
        Assert.Equal(AttackOutcome.Cancelled,f.Combat.Results[f.Player].Outcome);Assert.Null(f.Actor.Action);Assert.Equal(ready,f.Actor.ReadyAt);
        Assert.False(f.Combat.QueueBasicControl(f.Player,new(2,1,action.ExecutionId,BasicControlKind.Cancel,Vector2.UnitY),f.Tick));
    }

    [Fact]public void SameTickCancelSupersedesAimAndRemovesTheContinuousLease()
    {
        var f=new BasicActionPhaseTests.Fight();f.Begin(BasicAttackMode.BeginContinuous);var execution=f.Actor.Action!.ExecutionId;
        Assert.True(f.Combat.QueueBasicControl(f.Player,new(1,1,execution,BasicControlKind.Aim,Vector2.UnitX),f.Tick));
        Assert.True(f.Combat.QueueBasicControl(f.Player,new(2,1,execution,BasicControlKind.Cancel,Vector2.UnitY),f.Tick));
        Assert.False(f.Combat.QueueBasicControl(f.Player,new(3,1,execution,BasicControlKind.Aim,Vector2.UnitX),f.Tick));f.Step();
        Assert.Null(f.Actor.Action);Assert.False(f.Combat.HasContinuousBasic(f.Player));f.Step(1);Assert.Empty(f.Combat.Events);
    }

    [Fact]public void RebindKeepsBasicAndControlFloorsAndRejectsCrossFamilyCancellation()
    {
        var f=new ChannelingTests.Fight(BasicActionPhaseTests.Document());
        Assert.True(f.World.TryQueueAttack(42,new(90,0,Vector2.UnitY)));f.Step();var action=f.Actor.Action!;
        Assert.True(f.World.TryQueueBasicControl(42,new(70,90,action.ExecutionId,BasicControlKind.Aim,Vector2.UnitX)));f.Step();
        f.World.RebindConnection(42,84,new(2));var floor=f.Combat.BasicInputState(f.Id,f.World.Tick);
        Assert.Equal(90u,floor.LastInputSequence);Assert.Equal(70u,floor.LastControlSequence);Assert.Same(action,f.Actor.Action);
        Assert.False(f.World.TryQueueAttack(84,new(1,0,Vector2.UnitY)));
        Assert.False(f.World.TryQueueBasicControl(84,new(1,90,action.ExecutionId,BasicControlKind.Cancel,Vector2.UnitY)));
        Assert.False(f.World.TryQueueBasicControl(42,new(71,90,action.ExecutionId,BasicControlKind.Cancel,Vector2.UnitY)));
        Assert.True(f.World.TryQueueActionCancel(84,new(1,90,action.ExecutionId)));f.Step();
        Assert.Same(action,f.Actor.Action);Assert.Equal(AbilityOutcome.CancelDenied,f.Abilities.Results[f.Id].Outcome);
        Assert.True(f.World.TryQueueBasicControl(84,new(71,90,action.ExecutionId,BasicControlKind.Cancel,Vector2.UnitY)));f.Step();
        Assert.Null(f.Actor.Action);Assert.Equal(AttackOutcome.Cancelled,f.Combat.Results[f.Id].Outcome);
    }
}
