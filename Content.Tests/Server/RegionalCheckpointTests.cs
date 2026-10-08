using Content.Database;
using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;

namespace Content.Tests.Server;

public sealed class RegionalCheckpointTests
{
    private static WorldNodeSave Write(WorldNodeSession lease, SavedWorldNode state) =>
        new(lease, state, [new DatabaseWorldAudit("test", "RegionalCheckpoint", "test", 1)]);

    [Fact]
    public async Task DuplicateRegionalOwnerWriteIsRejectedWithoutPublishingCharacterChanges()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        var initial = PvpTests.Initial(PvpTests.World());
        await using var character = await store.OpenAsync("", initial, default);
        await using var region = await store.OpenWorldAsync("region_a", new(), default);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveRegionalCheckpointAsync(
            [new(character, initial with { Health = 1 })], [Write(region, new()), Write(region, new())], null, default));
        Assert.Equal(initial.Health, store.SingleState.Health);
    }

    [Fact]
    public async Task DuplicateItemAcrossTwoRegionsRollsBackEveryWorldAndCharacter()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        var initial = PvpTests.Initial(PvpTests.World());
        await using var character = await store.OpenAsync("", initial, default);
        await using var first = await store.OpenWorldAsync("region_a", new(), default);
        await using var second = await store.OpenWorldAsync("region_b", new(), default);
        var item = new SavedItem(Guid.NewGuid(), "test_sword_item", EquipmentSlot.None);
        var state = new SavedWorldNode { DeathLoot = [new(item, 0, 0, 100000)] };
        await Assert.ThrowsAnyAsync<Exception>(() => store.SaveRegionalCheckpointAsync(
            [new(character, initial with { Health = 1 })], [Write(first, state), Write(second, state)], null, default));
        Assert.Equal(initial.Health, store.SingleState.Health);
        using var c = WorldNodeStorageTests.Connect(store.DatabasePath); using var q = c.CreateCommand();
        q.CommandText = "SELECT count(*) FROM world_audit"; Assert.Equal(0L, q.ExecuteScalar());
        q.CommandText = "SELECT count(*) FROM item_owners WHERE instance_id=$id";
        q.Parameters.AddWithValue("$id", item.InstanceId.ToString()); Assert.Equal(0L, q.ExecuteScalar());
    }

    [Fact]
    public async Task AtomicOwnerSetSyncDoesNotDestroyAnInstanceMovedBetweenOwnedWorldRecords()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        await using var first = await store.OpenWorldAsync("region_a", new(), default);
        await using var second = await store.OpenWorldAsync("region_b", new(), default);
        var item = new SavedItem(Guid.NewGuid(), "test_sword_item", EquipmentSlot.None);
        var state = new SavedWorldNode { DeathLoot = [new(item, 0, 0, 100000)] };
        await store.SaveRegionalCheckpointAsync([], [Write(first, state), Write(second, new())], null, default);
        first.Revision++; second.Revision++;
        await store.SaveRegionalCheckpointAsync([], [Write(first, new()), Write(second, state)], null, default);
        using var c = WorldNodeStorageTests.Connect(store.DatabasePath); using var q = c.CreateCommand();
        q.CommandText = "SELECT owner_key FROM item_owners WHERE instance_id=$id";
        q.Parameters.AddWithValue("$id", item.InstanceId.ToString()); Assert.Equal("w:region_b", q.ExecuteScalar());
    }
}
