using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Items;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class DevelopmentReviveTests
{
    [Fact]
    public void TickRestoresOnlyDeadOwnerHealthWithoutResettingManaGearOrCooldowns()
    {
        var world = InventoryTests.World();
        var saved = world.CreateInitialCharacter() with { Health = 0, Mana = 12, AttackCooldownSeconds = 60 };
        var player = world.AddPlayer(1, new(1), saved);
        Assert.False(world.TryQueueDevelopmentRevive(1, new(1)));
        Assert.False(world.TryQueueDevelopmentRevive(999, new(1), authorized: true));
        Assert.True(world.TryQueueDevelopmentRevive(1, new(1), authorized: true));
        Assert.False(world.TryQueueDevelopmentRevive(1, new(1), authorized: true));
        Assert.Equal(0, world.Combat!.Get(player.EntityId).Health);
        world.Simulate(.05f);
        var actual = world.CaptureCharacter(1);
        Assert.Equal(world.Combat.Get(player.EntityId).Stats.MaxHealth, actual.Health);
        Assert.Equal(12, actual.Mana); Assert.True(actual.AttackCooldownSeconds > 59);
        Assert.Equal(saved.Inventory!.Items, actual.Inventory!.Items);
        Assert.False(world.TryQueueDevelopmentRevive(1, new(2), authorized: true));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public async Task NetworkChecksEnvironmentAndOnlyPublishesRevivedHealthAfterSQLiteCommit(string environment, bool permitted)
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var world = InventoryTests.World(); var maximumHealth = world.CreateInitialCharacter().Health;
        var initial = world.CreateInitialCharacter() with { Health = 0, Mana = 12 };
        string token;
        await using (var session = await store.OpenAsync("", initial, CancellationToken.None)) token = session.IssuedToken;
        var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new HandshakeCoordinator(), world, NullLogger<GameServerService>.Instance, store, environment: new TestEnvironment(environment));
        await server.StartAsync(CancellationToken.None);
        using var client = new NetworkMovementIntegrationTests.TestClient(port, token);
        TaskCompletionSource? gate = null;
        try
        {
            await CharacterPersistenceTests.Poll(client, () => client.CombatStates.Values.Any(state => state.Kind == CombatEntityKind.Player));
            var id = client.LocalSpawn.EntityId;
            Assert.Equal(permitted, client.CanDevelopmentRevive); Assert.Equal(0, client.CombatStates[id].Health);
            if (permitted) gate = store.PauseSaves();
            client.Revive(1);
            if (permitted)
            {
                await CharacterPersistenceTests.Poll(client, () => store.PendingSave);
                for (var i = 0; i < 20; i++) { client.Poll(); await Task.Delay(25); }
                Assert.Equal(0, client.CombatStates[id].Health); Assert.Equal(0, store.SingleState.Health);
                gate!.TrySetResult();
                await CharacterPersistenceTests.Poll(client, () => client.CombatStates[id].Health > 0);
            }
            else for (var i = 0; i < 40; i++) { client.Poll(); await Task.Delay(25); }
            Assert.Equal(permitted ? client.CombatStates[id].MaxHealth : 0, client.CombatStates[id].Health);
        }
        finally { gate?.TrySetResult(); await server.StopAsync(CancellationToken.None); }
        await using var restored = await store.OpenAsync(token, initial, CancellationToken.None);
        Assert.Equal(permitted ? maximumHealth : 0, restored.State.Health);
        Assert.Equal(12, restored.State.Mana);
    }
    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "revive-tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
