using Content.Server.Regions;
using Xunit;

namespace Content.Tests.Server;

public sealed class RegionOwnershipTests
{
    [Fact]
    public void CommitChangesOwnerButCommandsStayFrozenUntilActivation()
    {
        var registry = new RegionOwnership(); var character = Guid.NewGuid();
        registry.Register(character, "prototype"); var old = registry.Owner(character);
        var transfer = registry.Prepare(character, Guid.NewGuid(), old, "outskirts");
        Assert.False(registry.CanExecute(character, old));
        Assert.Equal(old, registry.Owner(character));
        registry.ConfirmCommit(transfer);
        Assert.Equal(transfer.Destination, registry.Owner(character));
        Assert.False(registry.CanExecute(character, transfer.Destination));
        Assert.Throws<InvalidOperationException>(() => registry.CancelBeforeCommit(transfer));
        registry.Complete(transfer);
        Assert.True(registry.CanExecute(character, transfer.Destination));
        Assert.False(registry.CanExecute(character, old));
        Assert.Throws<InvalidOperationException>(() => registry.ConfirmCommit(transfer));
    }

    [Fact]
    public void KnownRollbackRestoresSourceAndRejectsDuplicatePreparation()
    {
        var registry = new RegionOwnership(); var character = Guid.NewGuid();
        registry.Register(character, "prototype"); var old = registry.Owner(character);
        var transfer = registry.Prepare(character, Guid.NewGuid(), old, "outskirts");
        Assert.Throws<InvalidOperationException>(() => registry.Prepare(character, transfer.Operation, old, "outskirts"));
        Assert.Throws<InvalidOperationException>(() => registry.Unregister(character));
        registry.CancelBeforeCommit(transfer);
        Assert.True(registry.CanExecute(character, old));
        Assert.Throws<InvalidOperationException>(() => registry.ConfirmCommit(transfer));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UncertainWriteOutcomeCannotReactivateEitherRegion(bool committed)
    {
        var registry = new RegionOwnership(); var character = Guid.NewGuid();
        registry.Register(character, "prototype"); var old = registry.Owner(character);
        var transfer = registry.Prepare(character, Guid.NewGuid(), old, "outskirts");
        registry.RequireRecovery(transfer);
        Assert.False(registry.CanExecute(character, old));
        Assert.False(registry.CanExecute(character, transfer.Destination));
        Assert.Throws<InvalidOperationException>(() => registry.CancelBeforeCommit(transfer));
        registry.ResolveRecovery(transfer, committed);
        if (committed)
        {
            registry.RequireRecovery(transfer);
            Assert.Throws<InvalidOperationException>(() => registry.ResolveRecovery(transfer, false));
            registry.ResolveRecovery(transfer, true);
        }
        if (committed) registry.Complete(transfer); else registry.CancelBeforeCommit(transfer);
        Assert.True(registry.CanExecute(character, committed ? transfer.Destination : old));
    }

    [Fact]
    public void TransferTokenFromAnotherRegistryCannotChangeOwnership()
    {
        var first = new RegionOwnership(); var second = new RegionOwnership();
        var character = Guid.NewGuid(); first.Register(character, "prototype"); second.Register(character, "prototype");
        var a = first.Prepare(character, Guid.NewGuid(), first.Owner(character), "outskirts");
        var b = second.Prepare(character, a.Operation, second.Owner(character), "outskirts");
        Assert.Throws<InvalidOperationException>(() => second.ConfirmCommit(a));
        Assert.Equal(RegionTransferPhase.Prepared, b.Phase);
        Assert.Equal("prototype", second.Owner(character).Region);
    }

    [Fact]
    public void ActivationFailureCanRecoverOnlyAfterCheckingDurableDestination()
    {
        var registry = new RegionOwnership(); var character = Guid.NewGuid();
        registry.Register(character, "prototype");
        var transfer = registry.Prepare(character, Guid.NewGuid(), registry.Owner(character), "outskirts");
        registry.ConfirmCommit(transfer); registry.RequireRecovery(transfer);
        Assert.False(registry.CanExecute(character, transfer.Source));
        Assert.Throws<InvalidOperationException>(() => registry.ResolveRecovery(transfer, false));
        registry.ResolveRecovery(transfer, true); registry.Complete(transfer);
        Assert.True(registry.CanExecute(character, transfer.Destination));
    }

    [Fact]
    public void ReconnectDoesNotReuseAnOldSessionEpoch()
    {
        var registry = new RegionOwnership(); var character = Guid.NewGuid();
        registry.Register(character, "prototype"); var old = registry.Owner(character);
        registry.Unregister(character); registry.Register(character, "prototype");
        Assert.False(registry.CanExecute(character, old));
        Assert.True(registry.CanExecute(character, registry.Owner(character)));
    }

    [Fact]
    public void OldTransferCannotCompleteANewerJourney()
    {
        var registry = new RegionOwnership(); var character = Guid.NewGuid();
        registry.Register(character, "prototype");
        var first = registry.Prepare(character, Guid.NewGuid(), registry.Owner(character), "outskirts");
        registry.ConfirmCommit(first); registry.Complete(first);
        var second = registry.Prepare(character, Guid.NewGuid(), registry.Owner(character), "prototype");
        Assert.Equal(3UL, second.Destination.Epoch);
        Assert.Throws<InvalidOperationException>(() => registry.Complete(first));
        Assert.False(registry.CanExecute(character, second.Source));
    }

    [Fact]
    public void BudgetIdentityRegionAndExpectedOwnerAreValidated()
    {
        var registry = new RegionOwnership(1); var character = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => registry.Register(Guid.Empty, "prototype"));
        Assert.Throws<ArgumentException>(() => registry.Register(character, "../prototype"));
        registry.Register(character, "prototype");
        Assert.Throws<InvalidOperationException>(() => registry.Register(character, "prototype"));
        Assert.Throws<InvalidOperationException>(() => registry.Register(Guid.NewGuid(), "prototype"));
        Assert.Throws<InvalidOperationException>(() => registry.Prepare(character, Guid.Empty, registry.Owner(character), "outskirts"));
        Assert.Throws<InvalidOperationException>(() => registry.Prepare(character, Guid.NewGuid(), new("prototype", 2), "outskirts"));
        Assert.Throws<InvalidOperationException>(() => registry.Prepare(character, Guid.NewGuid(), registry.Owner(character), "prototype"));
    }
}
