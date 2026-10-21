using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ActionIdentityTests
{
    [Fact]public void ReattachedActorKeepsTheAbilitySequenceFloorAndStaleExecutionCannotCancelItsNewAction()
    {
        var f=new ChannelingTests.Fight(ChargedAbilityTests.Document());var aim=f.Combat.Get(f.World.TrainingTargetId).Position;
        Assert.True(f.World.TryQueueAbility(42,new(80,0,2,aim),0));f.Step();var old=Assert.Single(f.Abilities.ActiveStates);
        f.World.RebindConnection(42,84,new(2));Assert.Equal(80u,f.Abilities.Loadout(f.Id,f.World.Tick).LastInputSequence);
        Assert.False(f.World.TryQueueAbility(84,new(1,0,2,aim),0));
        Assert.True(f.World.TryQueueAbility(84,new(81,0,1,System.Numerics.Vector2.UnitX),0));f.Step();var current=f.Actor.Action;
        Assert.NotNull(current);Assert.NotEqual(old.EffectId,current.ExecutionId);
        Assert.True(f.World.TryQueueActionCancel(84,new(1,81,old.EffectId)));f.Step();
        Assert.Same(current,f.Actor.Action);Assert.Equal(AbilityOutcome.CancelDenied,f.Abilities.Results[f.Id].Outcome);
        Assert.True(f.World.TryQueueActionCancel(84,new(2,81,current.ExecutionId)));f.Step();
        Assert.Equal(AbilityOutcome.Cancelled,f.Abilities.Results[f.Id].Outcome);Assert.Null(f.Actor.Action);
    }

    [Fact]public void ExplicitUnknownExecutionCannotDeleteAnUnacknowledgedPendingInput()
    {
        var f=new ChannelingTests.Fight(ChargedAbilityTests.Document());var aim=f.Combat.Get(f.World.TrainingTargetId).Position;
        Assert.True(f.World.TryQueueAbility(42,new(1,0,2,aim),0));Assert.True(f.World.TryQueueActionCancel(42,new(1,1,ulong.MaxValue)));f.Step();
        Assert.Equal(AbilityOutcome.Accepted,f.Abilities.Results[f.Id].Outcome);Assert.Equal(AbilityPhase.Charging,Assert.Single(f.Abilities.ActiveStates).Phase);
        Assert.True(f.World.TryQueueAbilityControl(42,new(1,1,f.World.Tick,AbilityControlKind.Release,aim,ExecutionId:ulong.MaxValue)));f.Step();
        Assert.Equal(AbilityPhase.Charging,Assert.Single(f.Abilities.ActiveStates).Phase);Assert.Equal(100,f.Mana);
        var execution=Assert.Single(f.Abilities.ActiveStates).EffectId;
        Assert.True(f.World.TryQueueAbilityControl(42,new(2,1,f.World.Tick,AbilityControlKind.Release,aim,ExecutionId:execution)));f.Step();
        Assert.Equal(98,f.Mana);Assert.Equal(AbilityPhase.Impact,Assert.Single(f.Abilities.ActiveStates).Phase);
    }

    [Fact]public void ExecutionTargetIsValidatedAgainstItsOwnerEvenWhenInputSequencesCoincide()
    {
        var f=new ChannelingTests.Fight(ChargedAbilityTests.Document());var other=f.World.AddPlayer(43,new(2));
        var aim=f.Combat.Get(f.World.TrainingTargetId).Position;
        Assert.True(f.World.TryQueueAbility(42,new(1,0,2,aim),0));f.Step();var execution=Assert.Single(f.Abilities.ActiveStates).EffectId;
        Assert.True(f.World.TryQueueActionCancel(43,new(1,1,execution)));f.Step();
        Assert.Equal(AbilityOutcome.CancelDenied,f.Abilities.Results[other.EntityId].Outcome);Assert.NotNull(f.Actor.Action);
    }
}
