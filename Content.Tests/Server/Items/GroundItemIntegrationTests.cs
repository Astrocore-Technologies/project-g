using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Items;

public sealed class GroundItemIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCommitDoesNotPublishPickupOrGrantItOnShutdown(bool synchronous)
    {
        var sqlite = new SqliteCharacterStore(); var store = new FailingStore(sqlite, synchronous);
        var port = CharacterPersistenceTests.FreePort(); var initial = GroundItemTests.World().CreateInitialCharacter();
        var world = GroundItemTests.World(initial.X, initial.Z);
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new HandshakeCoordinator(), world, NullLogger<GameServerService>.Instance, store);
        await server.StartAsync(CancellationToken.None);
        using var client = new NetworkMovementIntegrationTests.TestClient(port);
        await CharacterPersistenceTests.Poll(client, () => client.GroundItems.Count == 1 && client.Inventories.Count == 1);
        var token = client.Token;
        store.Fail = true; client.Pickup(1, Assert.Single(client.GroundItems).Key);
        await CharacterPersistenceTests.Poll(client, () => server.ExecuteTask!.IsCompleted);
        await Assert.ThrowsAnyAsync<Exception>(() => server.ExecuteTask!);
        Assert.Empty(client.PickupResults); Assert.Single(client.GroundItems);
        Assert.Equal(2, Assert.Single(client.Inventories).Value.Items.Count);
        var restarted = new SqliteCharacterStore(sqlite.DatabasePath);
        Assert.Single(await restarted.LoadGroundItemsAsync(world.GroundItems!.Seeds, CancellationToken.None));
        await using var restored = await restarted.OpenAsync(token, initial, CancellationToken.None);
        Assert.Equal(2, restored.State.Inventory!.Items.Length);
        await server.StopAsync(CancellationToken.None);
    }
    private sealed class FailingStore(ICharacterStore inner, bool synchronous) : ICharacterStore
    {
        internal bool Fail;
        public Task InitializeAsync(CancellationToken token) => inner.InitializeAsync(token);
        public Task<CharacterSession> OpenAsync(string token, CharacterState initial, CancellationToken cancellation) => inner.OpenAsync(token, initial, cancellation);
        public Task<IReadOnlyList<SavedGroundItem>> LoadGroundItemsAsync(IReadOnlyList<SavedGroundItem> seeds, CancellationToken token) => inner.LoadGroundItemsAsync(seeds, token);
        public Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken token)
        {
            if (!Fail) return inner.SaveAsync(changes, token);
            if (synchronous) throw new IOException("Injected immediate adapter failure.");
            return Task.FromException(new IOException("Injected async commit failure."));
        }
    }

    [Fact]
    public async Task LossyClientsRaceForOneItemAndSeeNothingUntilCommitThenRestartRestoresWinner()
    {
        var store = new SqliteCharacterStore(); var port = CharacterPersistenceTests.FreePort();
        // Put fixture at the initial spawn so neither test client needs to invent its position.
        var probe = GroundItemTests.World(); var initial = probe.CreateInitialCharacter();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new HandshakeCoordinator(), GroundItemTests.World(initial.X, initial.Z), NullLogger<GameServerService>.Instance, store);
        await server.StartAsync(CancellationToken.None);
        using var first = new NetworkMovementIntegrationTests.TestClient(port);
        using var second = new NetworkMovementIntegrationTests.TestClient(port);
        async Task Poll(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested(); first.Poll(); second.Poll();
                if (condition()) return;
                await Task.Delay(25, timeout.Token);
            }
        }
        string winnerToken;
        try
        {
            await Poll(() => first.GroundItems.Count == 1 && second.GroundItems.Count == 1 && first.Inventories.Count == 1 && second.Inventories.Count == 1);
            var handle = Assert.Single(first.GroundItems).Key; Assert.Equal(handle, Assert.Single(second.GroundItems).Key);
            var gate = store.PauseSaves();
            try
            {
                first.Pickup(1, handle); await Poll(() => store.PendingSave);
                second.Pickup(1, handle);
                for (var i = 0; i < 25; i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                Assert.Empty(first.PickupResults); Assert.Empty(second.PickupResults);
                Assert.Single(first.GroundItems); Assert.Single(second.GroundItems);
                Assert.Equal(2, Assert.Single(first.Inventories).Value.Items.Count);
                gate.TrySetResult();
                await Poll(() => first.PickupResults.Count == 1 && second.PickupResults.Count == 1 && first.GroundItems.Count == 0 && second.GroundItems.Count == 0);
                // A pending save can precede pickup delivery; reliable ordering is per peer,
                // so packet loss may let either client win this cross-peer race.
                Assert.Equal(1, new[] { first, second }.Count(client => client.PickupResults[0].Outcome == PickupOutcome.Accepted));
                var winner = first.PickupResults[0].Outcome == PickupOutcome.Accepted ? first : second;
                var loser = ReferenceEquals(winner, first) ? second : first;
                Assert.Equal(PickupOutcome.Missing, loser.PickupResults[0].Outcome);
                await Poll(() => Assert.Single(winner.Inventories).Value.Items.Count == 3);
                Assert.Equal(2, Assert.Single(loser.Inventories).Value.Items.Count);
                winner.Pickup(2, handle); await Poll(() => winner.PickupResults.Count == 2);
                Assert.Equal(PickupOutcome.Missing, winner.PickupResults[1].Outcome);
                winnerToken = winner.Token;
            }
            finally { gate.TrySetResult(); }
        }
        finally { await server.StopAsync(CancellationToken.None); }
        var restarted = GroundItemTests.World(initial.X, initial.Z);
        var adapter = new SqliteCharacterStore(store.DatabasePath); await adapter.InitializeAsync(CancellationToken.None);
        Assert.Empty(await adapter.LoadGroundItemsAsync(restarted.GroundItems!.Seeds, CancellationToken.None));
        await using var restored = await adapter.OpenAsync(winnerToken, restarted.CreateInitialCharacter(), CancellationToken.None);
        Assert.Equal(3, restored.State.Inventory!.Items.Length);
    }
}
