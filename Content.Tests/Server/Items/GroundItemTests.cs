using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Items;

public sealed class GroundItemTests
{
    internal static ServerWorld World(float x = -5, float z = 1, float range = 2, bool wall = false) => new(
        Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
        navigation: Options.Create(new NavigationOptions { BlockedAreas = wall ? [new() { X = 14, Z = 10, Width = 2, Height = 10 }] : [] }),
        catalog: ContentCatalogTests.Load(),
        inventory: Options.Create(new InventoryOptions { Enabled = true }),
        groundItems: Options.Create(new GroundItemOptions { Enabled = true, PickupRange = range,
            Seeds = [new() { Id = "test-ground-sword", DefinitionId = "test_sword_item", X = x, Z = z }] }));
    private static void RestoreSeeds(ServerWorld world) => world.GroundItems!.Restore(world.GroundItems.Seeds);
    private static ulong Handle(ServerWorld world, Vector2 position)
    {
        var candidates = new HashSet<NetworkEntityId>(); world.GroundItems!.Query(position, 10, candidates);
        return Assert.Single(candidates).Value;
    }

    [Fact]
    public void SimultaneousPickupHasOneWinnerAndReplayDoesNotDuplicate()
    {
        var world = World(); RestoreSeeds(world);
        var initial = world.CreateInitialCharacter() with { X = -5, Z = 1 };
        var first = world.AddPlayer(1, new(1), initial); var second = world.AddPlayer(2, new(2), world.CreateInitialCharacter() with { X = -5, Z = 1 });
        var handle = Handle(world, first.Position);
        Assert.True(world.TryQueuePickup(1, new(1, handle))); Assert.True(world.TryQueuePickup(2, new(1, handle)));
        Assert.Equal(2, world.Inventory!.Capture(first.EntityId).Items.Length);
        world.Simulate(.05f);
        Assert.Equal(PickupOutcome.Accepted, world.GroundItems!.Results[first.EntityId].Outcome);
        Assert.Equal(PickupOutcome.Missing, world.GroundItems.Results[second.EntityId].Outcome);
        Assert.Equal(3, world.Inventory.Capture(first.EntityId).Items.Length);
        Assert.Equal(2, world.Inventory.Capture(second.EntityId).Items.Length);
        Assert.Equal(Assert.Single(world.PickupClaims(1)), world.Inventory.Capture(first.EntityId).Items[^1].InstanceId);
        Assert.False(world.TryQueuePickup(1, new(1, handle)));
        Assert.True(world.TryQueuePickup(1, new(2, handle))); world.Simulate(.05f);
        Assert.Equal(PickupOutcome.Missing, world.GroundItems.Results[first.EntityId].Outcome);
        Assert.Equal(3, world.Inventory.Capture(first.EntityId).Items.Length);
    }

    [Theory]
    [InlineData(-9, 1, 100, PickupOutcome.OutOfRange)]
    [InlineData(-5, 1, 0, PickupOutcome.InvalidState)]
    public void DistanceAndAliveAreAuthoritative(float x, float z, double health, PickupOutcome expected)
    {
        var world = World(); RestoreSeeds(world);
        var player = world.AddPlayer(1, new(1), world.CreateInitialCharacter() with { X = x, Z = z, Health = health });
        var handle = Handle(world, new(-5, 1)); world.TryQueuePickup(1, new(1, handle)); world.Simulate(.05f);
        Assert.Equal(expected, world.GroundItems!.Results[player.EntityId].Outcome);
        Assert.Equal(2, world.Inventory!.Capture(player.EntityId).Items.Length);
    }

    [Fact]
    public void WallsCapacityAndFloodRejectWithoutRemovingGroundItem()
    {
        var blocked = World(1.5f, 0, 5, wall: true); RestoreSeeds(blocked);
        var first = blocked.AddPlayer(1, new(1), blocked.CreateInitialCharacter() with { X = -1.5f, Z = 0 });
        var handle = Handle(blocked, first.Position);
        blocked.TryQueuePickup(1, new(1, handle)); blocked.Simulate(.05f);
        Assert.Equal(PickupOutcome.Blocked, blocked.GroundItems!.Results[first.EntityId].Outcome);
        var world = World(); RestoreSeeds(world);
        var full = new SavedInventory { Items = Enumerable.Range(0, 8).Select(_ => new SavedItem(Guid.NewGuid(), "test_sword_item", EquipmentSlot.None)).ToArray() };
        var player = world.AddPlayer(1, new(1), world.CreateInitialCharacter() with { X = -5, Z = 1, Inventory = full });
        handle = Handle(world, player.Position);
        world.TryQueuePickup(1, new(1, handle)); world.Simulate(.05f);
        Assert.Equal(PickupOutcome.InventoryFull, world.GroundItems!.Results[player.EntityId].Outcome);
        world.TryQueuePickup(1, new(2, handle)); Assert.False(world.TryQueuePickup(1, new(3, handle))); world.Simulate(.05f);
        Assert.Equal(PickupOutcome.RateLimited, world.GroundItems.Results[player.EntityId].Outcome);
        Assert.Equal(handle, Handle(world, player.Position));
    }

    [Fact]
    public void GroundAOIHasEnterExitAndRemovalWithoutGlobalActorScan()
    {
        var world = World(); RestoreSeeds(world);
        world.AddPlayer(1, new(1), world.CreateInitialCharacter() with { X = -5, Z = 1 });
        var view = new InterestView(); world.UpdateGroundInterest(1, view);
        Assert.Single(view.GroundItemEntries);
        world.TryQueuePickup(1, new(1, view.GroundItemEntries[0].Value)); world.Simulate(.05f); world.UpdateGroundInterest(1, view);
        Assert.Single(view.GroundItemExits); Assert.Empty(view.GroundItemEntities);
        world.UpdateGroundInterest(1, view); Assert.Empty(view.GroundItemExits);
    }

    [Fact]
    public async Task SQLiteClaimAndInventoryCommitTogetherAndConsumedSeedDoesNotRespawn()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var world = World(); var ground = await store.LoadGroundItemsAsync(world.GroundItems!.Seeds, CancellationToken.None);
        world.GroundItems.Restore(ground);
        var initial = world.CreateInitialCharacter() with { X = -5, Z = 1 };
        string token;
        await using (var session = await store.OpenAsync("", initial, CancellationToken.None))
        {
            token = session.IssuedToken; var player = world.AddPlayer(1, new(1), session.State);
            world.TryQueuePickup(1, new(1, Handle(world, player.Position))); world.Simulate(.05f);
            await store.SaveAsync([new(session, world.CaptureCharacter(1), world.PickupClaims(1))], CancellationToken.None);
        }
        var restarted = World(); var adapter = new SqliteCharacterStore(store.DatabasePath);
        await adapter.InitializeAsync(CancellationToken.None);
        Assert.Empty(await adapter.LoadGroundItemsAsync(restarted.GroundItems!.Seeds, CancellationToken.None));
        await using var reopened = await adapter.OpenAsync(token, restarted.CreateInitialCharacter(), CancellationToken.None);
        Assert.Equal(3, reopened.State.Inventory!.Items.Length);
        Assert.Equal(ground[0].InstanceId, reopened.State.Inventory.Items[^1].InstanceId);
    }

    [Fact]
    public void GroundAOIExitAndReentryProduceOneLifecycleEventEach()
    {
        var world = World(); RestoreSeeds(world);
        world.AddPlayer(1, new(1), world.CreateInitialCharacter() with { X = -5, Z = 1 });
        var view = new InterestView(); world.UpdateGroundInterest(1, view);
        var handle = Assert.Single(view.GroundItemEntries);
        Assert.True(world.TryApplyMove(1, new(1, 0, new Vector2(14, 10))));
        for (var i = 0; i < 120; i++) world.Simulate(.05f);
        world.UpdateGroundInterest(1, view); Assert.Equal(handle, Assert.Single(view.GroundItemExits));
        world.UpdateGroundInterest(1, view); Assert.Empty(view.GroundItemExits); Assert.Empty(view.GroundItemEntries);
        Assert.True(world.TryApplyMove(1, new(2, 0, new Vector2(-5, 1))));
        for (var i = 0; i < 120; i++) world.Simulate(.05f);
        world.UpdateGroundInterest(1, view); Assert.Equal(handle, Assert.Single(view.GroundItemEntries));
    }

    [Fact]
    public async Task ConflictingClaimRollsBackOtherInventoryAndCharacterWrites()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var world = World(); var ground = Assert.Single(await store.LoadGroundItemsAsync(world.GroundItems!.Seeds, CancellationToken.None));
        var initial = world.CreateInitialCharacter();
        await using var first = await store.OpenAsync("", initial, CancellationToken.None);
        await using var second = await store.OpenAsync("", initial, CancellationToken.None);
        await using var third = await store.OpenAsync("", initial, CancellationToken.None);
        var changed = initial with { Health = 19, Inventory = new() { Items = [.. initial.Inventory!.Items, new(ground.InstanceId, ground.DefinitionId, EquipmentSlot.None)] } };
        await store.SaveAsync([new(first, changed, [ground.InstanceId])], CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([
            new(third, initial with { Health = 11 }), new(second, changed, [ground.InstanceId])], CancellationToken.None));
        var token = second.IssuedToken; await second.DisposeAsync();
        await using var restored = await store.OpenAsync(token, initial, CancellationToken.None);
        Assert.Equal(initial.Health, restored.State.Health); Assert.Equal(2, restored.State.Inventory!.Items.Length);
        var thirdToken = third.IssuedToken; await third.DisposeAsync();
        await using var other = await store.OpenAsync(thirdToken, initial, CancellationToken.None);
        Assert.Equal(initial.Health, other.State.Health);
    }
}
