using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Items;

public sealed class InventoryTests
{
    internal static ServerWorld World() => new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
        catalog: ContentCatalogTests.Load(), inventory: Options.Create(new InventoryOptions { Enabled = true }));

    [Fact]
    public void EquipmentRunsOnTickAndDoesNotHealOrResetCooldowns()
    {
        var world = World();
        var initial = world.CreateInitialCharacter() with { Health = 30, AttackCooldownSeconds = 60 };
        var player = world.AddPlayer(1, new(1), initial);
        var actor = world.Combat!.Get(player.EntityId);
        var stats = actor.Stats;
        var items = world.Inventory!.State(player.EntityId, 0).Items;
        Assert.Equal(2, items.Count);
        var sword = items.Single(item => item.Slot == EquipmentSlot.Weapon);
        Assert.True(world.TryQueueInventory(1, new(1, InventoryAction.Equip, sword.Handle)));
        Assert.Equal(stats, actor.Stats);
        world.Simulate(.05f);
        Assert.Equal(stats.MeleeAttack + 8, actor.Stats.MeleeAttack);
        Assert.Equal(30, actor.Health);
        Assert.True(world.CaptureCharacter(1).AttackCooldownSeconds > 59);
        Assert.False(world.TryQueueInventory(1, new(1, InventoryAction.Equip, sword.Handle)));
        var armor = items.Single(item => item.Slot == EquipmentSlot.Armor);
        Assert.True(world.TryQueueInventory(1, new(2, InventoryAction.Equip, armor.Handle)));
        world.Simulate(.05f);
        Assert.Equal(stats.PhysicalDefense + 8, actor.Stats.PhysicalDefense);
        Assert.Equal(stats.MaxHealth + 20, actor.Stats.MaxHealth);
        Assert.Equal(30, actor.Health);
        Assert.True(world.TryQueueInventory(1, new(3, InventoryAction.Unequip, armor.Handle)));
        world.Simulate(.05f);
        Assert.Equal(stats.MaxHealth, actor.Stats.MaxHealth);
        Assert.Equal(30, actor.Health);
        Assert.True(world.CaptureCharacter(1).AttackCooldownSeconds > 59);
    }

    [Fact]
    public void ForeignHandlesAndDeadActorsCannotEquip()
    {
        var world = World();
        var first = world.AddPlayer(1, new(1));
        var second = world.AddPlayer(2, new(2), world.CreateInitialCharacter() with { Health = 0 });
        var foreign = world.Inventory!.State(second.EntityId, 0).Items[0].Handle;
        Assert.True(world.TryQueueInventory(1, new(1, InventoryAction.Equip, foreign)));
        world.Simulate(.05f);
        Assert.Equal(InventoryOutcome.NotOwned, world.Inventory.Results[first.EntityId].Outcome);
        Assert.True(world.TryQueueInventory(2, new(1, InventoryAction.Equip, foreign)));
        world.Simulate(.05f);
        Assert.Equal(InventoryOutcome.InvalidState, world.Inventory.Results[second.EntityId].Outcome);
        Assert.All(world.Inventory.Capture(first.EntityId).Items, item => Assert.Equal(EquipmentSlot.None, item.EquippedSlot));
    }

    [Fact]
    public void FloodCancelsPendingEquipmentAndSavedSlotsAreValidated()
    {
        var world = World(); var player = world.AddPlayer(1, new(1));
        var handle = world.Inventory!.State(player.EntityId, 0).Items[0].Handle;
        Assert.True(world.TryQueueInventory(1, new(1, InventoryAction.Equip, handle)));
        Assert.False(world.TryQueueInventory(1, new(2, InventoryAction.Equip, handle)));
        world.Simulate(.05f);
        Assert.Equal(InventoryOutcome.RateLimited, world.Inventory.Results[player.EntityId].Outcome);
        Assert.All(world.Inventory.Capture(player.EntityId).Items, item => Assert.Equal(EquipmentSlot.None, item.EquippedSlot));
        var broken = world.CreateInitialCharacter() with { Inventory = new() { Items = [new(Guid.NewGuid(), "test_sword_item", EquipmentSlot.Armor)] } };
        Assert.Throws<InvalidDataException>(() => world.AddPlayer(2, new(2), broken));
        Assert.Single(world.Players);
    }

    [Fact]
    public async Task SQLiteRestoresInstancesAndEquipmentWithoutGrantingStartersAgain()
    {
        var world = World(); var initial = world.CreateInitialCharacter();
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        string token; SavedInventory saved;
        await using (var session = await store.OpenAsync("", initial, CancellationToken.None))
        {
            token = session.IssuedToken;
            var player = world.AddPlayer(1, new(1), session.State);
            var handle = world.Inventory!.State(player.EntityId, 0).Items[0].Handle;
            world.TryQueueInventory(1, new(1, InventoryAction.Equip, handle)); world.Simulate(.05f);
            var state = world.CaptureCharacter(1); saved = state.Inventory!;
            await store.SaveAsync([new(session, state)], CancellationToken.None);
        }
        await using var reopened = await store.OpenAsync(token, world.CreateInitialCharacter(), CancellationToken.None);
        Assert.Equal(saved.Items, reopened.State.Inventory!.Items);
        Assert.Equal(1, store.Count);
        var restored = World(); var restoredPlayer = restored.AddPlayer(1, new(1), reopened.State);
        Assert.Single(restored.Inventory!.State(restoredPlayer.EntityId, 0).Items, item => item.Equipped);
    }

    [Fact]
    public void SavedInventoryRejectsDuplicatesUnknownFieldsAndFutureModels()
    {
        var item = new SavedItem(Guid.NewGuid(), "test_sword_item", EquipmentSlot.None);
        Assert.Throws<InvalidDataException>(() => new SavedInventory { Items = [item, item] }.Validate());
        Assert.Throws<InvalidDataException>(() => new SavedInventory { Version = 2, Items = [] }.Validate());
        Assert.Throws<System.Text.Json.JsonException>(() => SavedInventory.Deserialize("{\"Version\":1,\"Items\":[],\"Cheat\":1}"));
    }

    [Fact]
    public async Task VersionOneDatabaseMigratesWithoutResettingCharacterAndBootstrapsInventoryOnce()
    {
        var store = new SqliteCharacterStore(); var initial = World().CreateInitialCharacter() with { Health = 17 };
        var id = Guid.NewGuid(); var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.DatabasePath)!);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            using var resource = typeof(Content.Database.ICharacterDatabase).Assembly.GetManifestResourceStream(
                "Content.Database.Migrations.0001_characters.sqlite.sql")!;
            using var reader = new StreamReader(resource);
            command.CommandText = "CREATE TABLE schema_version(version INTEGER PRIMARY KEY); INSERT INTO schema_version VALUES(1);" + reader.ReadToEnd();
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO characters VALUES($id,$hash,$owner,0,1,$state)";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$hash", System.Security.Cryptography.SHA256.HashData(Convert.FromHexString(token)));
            command.Parameters.AddWithValue("$owner", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$state", initial.Serialize()); command.ExecuteNonQuery();
        }
        await store.InitializeAsync(CancellationToken.None);
        SavedItem[] firstItems;
        await using (var session = await store.OpenAsync(token, World().CreateInitialCharacter(), CancellationToken.None))
        {
            Assert.Equal(id, session.CharacterId); Assert.Equal(17, session.State.Health);
            firstItems = session.State.Inventory!.Items; Assert.Equal(2, firstItems.Length);
        }
        await using var reopened = await store.OpenAsync(token, World().CreateInitialCharacter(), CancellationToken.None);
        Assert.Equal(firstItems, reopened.State.Inventory!.Items);
        Assert.Equal(17, reopened.State.Health);
    }

    [Fact]
    public void DatabaseDoesNotDependOnGameDomainOrGodot()
    {
        var references = typeof(Content.Database.ICharacterDatabase).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, assembly => assembly.Name is "Content.Server" or "Content.Shared" or "Project-G" || assembly.Name!.StartsWith("Godot"));
    }
}
