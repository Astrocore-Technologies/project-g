using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class AbilityBufferDeadlineTests
{
    private sealed class Fight
    {
        internal readonly ServerWorld World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>1);
        internal readonly NetworkEntityId Id;
        internal AbilitySimulation Abilities=>World.Abilities!;
        internal Fight(){Id=World.AddPlayer(42,new(1)).EntityId;World.Combat!.SetRecovery(Id,.14);}
        internal AbilityOutcome Result=>Abilities.Results[Id].Outcome;
        internal void Queue(uint sequence=1)=>Assert.True(World.TryQueueAbility(42,new(sequence,World.Tick,1,Vector2.UnitX),0));
        internal void Step(float delta=.05f)=>World.Simulate(delta);
        internal void Buffer(){Queue();Step();Assert.Equal(AbilityOutcome.Buffered,Result);Assert.Empty(Abilities.ActiveStates);}
    }

    [Fact]public void AnExpiredInputCannotExecuteJustBecauseItsLockEndsInTheSameStep()
    {
        var f=new Fight();f.World.Combat!.SetRecovery(f.Id,.155);f.Buffer();f.Step(.11f);
        Assert.Equal(AbilityOutcome.Busy,f.Result);Assert.Empty(f.Abilities.ActiveStates);Assert.Equal(100,f.Abilities.Mana(f.Id));
        f.Step();Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void ExactlyOneHundredFiftyMillisecondsIsStillInsideTheApprovedWindow()
    {
        var f=new Fight();f.Buffer();f.Step(.1f);
        Assert.Equal(AbilityOutcome.Accepted,f.Result);Assert.Equal(1u,Assert.Single(f.Abilities.ActiveStates).Sequence);
    }

    [Fact]public void AnotherServerStepIsNeverAddedToTheBufferLifetime()
    {
        var f=new Fight();f.Buffer();f.World.Combat!.SetRecovery(f.Id,.12);
        f.Step();f.Step();Assert.Empty(f.Abilities.ActiveStates);f.Step(.02f);
        Assert.Equal(AbilityOutcome.Busy,f.Result);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void AReplacementHasItsOwnReceiptDeadlineWithoutReplayingThePreviousInput()
    {
        var f=new Fight();f.Buffer();f.Queue(2);f.Step();Assert.Equal(AbilityOutcome.Buffered,f.Result);
        f.World.Combat!.SetRecovery(f.Id,.09);f.Step(.09f);
        Assert.Equal(AbilityOutcome.Accepted,f.Result);Assert.Equal(2u,Assert.Single(f.Abilities.ActiveStates).Sequence);
    }

    [Fact]public void ManualCancellationClearsTheSingleSlotWithoutCostOrLaterExecution()
    {
        var f=new Fight();f.Buffer();Assert.True(f.World.TryQueueActionCancel(42,new(1,1)));f.Step();
        Assert.Equal(AbilityOutcome.Cancelled,f.Result);f.Step(.2f);Assert.Empty(f.Abilities.ActiveStates);Assert.Equal(100,f.Abilities.Mana(f.Id));
    }

    [Fact]public void AStillValidBufferRechecksResourcesBeforeCreatingTheAction()
    {
        var f=new Fight();f.Buffer();f.World.Combat!.DrainMana!(f.Id,100);f.Step(.1f);
        Assert.Equal(AbilityOutcome.NoMana,f.Result);Assert.Empty(f.Abilities.ActiveStates);Assert.Equal(0,f.Abilities.Loadout(f.Id,f.World.Tick).Abilities.Single(s=>s.Id==1).ReadyInSeconds);
    }
}
