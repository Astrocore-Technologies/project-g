using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Networking;

public sealed class RecoveryReplicationTests
{
    private static GameServerService Server(int port, ServerWorld world, SqliteCharacterStore store) => new(
        Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
        new HandshakeCoordinator(), world, NullLogger<GameServerService>.Instance, store);

    private static async Task Poll(NetworkMovementIntegrationTests.TestClient first,
        NetworkMovementIntegrationTests.TestClient second, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        do
        {
            timeout.Token.ThrowIfCancellationRequested(); first.Poll(); second.Poll();
            if (condition()) return;
            await Task.Delay(25, timeout.Token);
        } while (true);
    }

    [Fact]
    public async Task TwoLossyClientsSeeHealedHpOnlyAfterCheckpointAndRestartRetainsIt()
    {
        var world = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            catalog: ContentCatalogTests.Load());
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        string credential;
        await using (var seed = await store.OpenAsync("", world.CreateInitialCharacter() with { Health = 10 }, default))
            credential = seed.IssuedToken;
        var port = CharacterPersistenceTests.FreePort();
        using var server = Server(port, world, store); await server.StartAsync(default);
        TaskCompletionSource? gate = null;
        double healed = 0;
        try
        {
            using var first = new NetworkMovementIntegrationTests.TestClient(port, credential);
            using var second = new NetworkMovementIntegrationTests.TestClient(port);
            await Poll(first, second, () => first.Spawns.Count == 2 && second.Spawns.Count == 2 && first.CombatStates.Count == 3 && second.CombatStates.Count == 3);
            var id = first.LocalSpawn.EntityId;
            gate = store.PauseSaves();
            await Poll(first, second, () => store.PendingSave);
            healed = world.Combat!.Get(id).Health;
            // Flush earlier reliable messages while the new HP is held behind the real SQLite commit.
            var until = DateTime.UtcNow.AddMilliseconds(600);
            await Poll(first, second, () => DateTime.UtcNow >= until);
            Assert.True(first.CombatStates[id].Health < healed);
            Assert.Equal(first.CombatStates[id].Health, second.CombatStates[id].Health);
            gate.TrySetResult();
            await Poll(first, second, () => first.CombatStates[id].Health >= healed && second.CombatStates[id].Health >= healed);
            Assert.Equal(first.CombatStates[id].MaxHealth, second.CombatStates[id].MaxHealth);
        }
        finally { gate?.TrySetResult(); await server.StopAsync(default); }
        var reopened = new SqliteCharacterStore(store.DatabasePath); await reopened.InitializeAsync(default);
        await using var restored = await reopened.OpenAsync(credential, world.CreateInitialCharacter(), default);
        Assert.True(restored.State.Health >= healed);
    }

    [Fact]
    public async Task TwoLossyClientsReplaceDeadNpcOnlyAfterDurableRespawn()
    {
        var f = new NpcRespawnTests.Encounter();
        // Seed the last committed death, then advance a thread-safe test clock at the deadline.
        long now = 100000;
        f.World.NpcRespawnClock = () => Interlocked.Read(ref now);
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        await using (await store.OpenWorldAsync("respawn-test", new() { NpcRespawns = [new(1, "test_spawn", 130000)] }, default)) { }
        var port = CharacterPersistenceTests.FreePort();
        using var server = Server(port, f.World, store); await server.StartAsync(default);
        TaskCompletionSource? gate = null;
        try
        {
            using var first = new NetworkMovementIntegrationTests.TestClient(port);
            using var second = new NetworkMovementIntegrationTests.TestClient(port);
            var oldId = f.Npc.Id;
            await Poll(first, second, () => first.CombatStates.ContainsKey(oldId) && second.CombatStates.ContainsKey(oldId));
            Assert.Equal(0, first.CombatStates[oldId].Health); Assert.Equal(0, second.CombatStates[oldId].Health);
            gate = store.PauseSaves(); Interlocked.Exchange(ref now, 130000);
            await Poll(first, second, () => store.PendingSave);
            var nextId = f.Npc.Id;
            Assert.NotEqual(oldId, nextId);
            Assert.False(first.CombatStates.ContainsKey(nextId)); Assert.False(second.CombatStates.ContainsKey(nextId));
            gate.TrySetResult();
            await Poll(first, second, () => first.CombatStates.ContainsKey(nextId) && second.CombatStates.ContainsKey(nextId) &&
                !first.CombatStates.ContainsKey(oldId) && !second.CombatStates.ContainsKey(oldId));
            Assert.Equal(first.CombatStates[nextId], second.CombatStates[nextId]);
            Assert.Equal(first.CombatStates[nextId].MaxHealth, first.CombatStates[nextId].Health);
            await Poll(first, second, () => first.HasSnapshot(nextId) && second.HasSnapshot(nextId));
        }
        finally { gate?.TrySetResult(); await server.StopAsync(default); }
    }
}
