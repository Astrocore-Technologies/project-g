using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Networking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class CharacterPersistenceTests
{
    internal static ServerWorld World() => new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
        catalog: ContentCatalogTests.Load());

    [Fact]
    public void StateRoundTripsAndRestoresStatsPositionResourcesAndCooldown()
    {
        var world = World();
        var saved = world.CreateInitialCharacter() with
        {
            X = -6, Z = 2, Stats = new BaseStats(20, 4, 10, 10, 10, 1), Health = 0, Mana = 12,
            AttackCooldownSeconds = 60,
            Defense = new SavedDefense(1,42,.4,.8),
            Cooldowns = world.CreateInitialCharacter().Cooldowns.Select(item => item with { Seconds = 60 }).ToArray()
        };
        var restored = CharacterState.Deserialize(saved.Serialize());
        var player = world.AddPlayer(10, new PlayerId(55), restored);
        var actual = world.CaptureCharacter(10);
        Assert.Equal(new Vector2(-6, 2), player.Position);
        Assert.Equal(saved.Stats, actual.Stats);
        Assert.Equal(0, actual.Health); Assert.Equal(12, actual.Mana);
        Assert.Equal(saved.Defense,actual.Defense);
        Assert.InRange(actual.AttackCooldownSeconds, 58, 60);
        Assert.All(actual.Cooldowns, item => Assert.InRange(item.Seconds, 58, 60));
        Assert.False(world.TryApplyMove(10, new MoveCommand(1, 0, new Vector2(-5, 2))));
        Assert.Equal(0u, player.LastProcessedSequence);
    }

    [Fact]
    public void OfflineCooldownElapsesWithoutHealingOrRestoringMana()
    {
        var world = World();
        var saved = world.CreateInitialCharacter() with
        {
            Health = 7, Mana = 2, AttackCooldownSeconds = 1,
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds(),
            Cooldowns = world.CreateInitialCharacter().Cooldowns.Select(item => item with { Seconds = 1 }).ToArray()
        };
        world.AddPlayer(1, new PlayerId(1), saved);
        var actual = world.CaptureCharacter(1);
        Assert.Equal(7, actual.Health); Assert.Equal(2, actual.Mana);
        Assert.Equal(0, actual.AttackCooldownSeconds);
        Assert.All(actual.Cooldowns, item => Assert.Equal(0, item.Seconds));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("missingVersion")]
    [InlineData("unknownField")]
    [InlineData("negativeStat")]
    [InlineData("negativeHealth")]
    [InlineData("missingHealth")]
    [InlineData("duplicateCooldown")]
    public void InvalidSavedModelFailsClosed(string problem)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(World().CreateInitialCharacter().Serialize())!.AsObject();
        switch (problem)
        {
            case "version": json["Version"] = 99; break;
            case "missingVersion": json.Remove("Version"); break;
            case "unknownField": json["Secret"] = 1; break;
            case "negativeStat": json["Stats"]!["Strength"] = -1; break;
            case "negativeHealth": json["Health"] = -1; break;
            case "missingHealth": json.Remove("Health"); break;
            case "duplicateCooldown": json["Cooldowns"]!.AsArray().Add(json["Cooldowns"]![0]!.DeepClone()); break;
        }
        Assert.ThrowsAny<Exception>(() => CharacterState.Deserialize(json.ToJsonString()));
    }

    [Fact]
    public void InvalidLoadedPositionOrLoadoutLeavesNoRuntimeEntity()
    {
        var world = World();
        var initial = world.CreateInitialCharacter();
        Assert.Throws<InvalidDataException>(() => world.AddPlayer(1, new PlayerId(1), initial with { X = 1000, Z = 0 }));
        Assert.Empty(world.Players);
        Assert.Throws<InvalidDataException>(() => world.AddPlayer(1, new PlayerId(1), initial with { Cooldowns = [] }));
        Assert.Empty(world.Players);
        Assert.NotNull(world.AddPlayer(1, new PlayerId(1), initial));
    }

    [Fact]
    public async Task ReconnectAndServerRestartRestoreCharacterWithoutDuplicateLogin()
    {
        var store = new SqliteCharacterStore();
        var port = FreePort();
        string token;
        Guid character;
        Vector2 position;
        NetworkEntityId firstEntity;
        using (var server = Server(port, store))
        {
            await server.StartAsync(CancellationToken.None);
            using var first = new NetworkMovementIntegrationTests.TestClient(port);
            await Poll(first, () => first.Spawns.Count != 0);
            token = first.Token;
            Assert.Equal(64, token.Length);
            character = store.SingleId;
            firstEntity = first.LocalSpawn.EntityId;
            uint sequence = 0;
            position = new(-6, 2);
            await Poll(first, () => { first.Move(++sequence, position); return first.IsAt(firstEntity, position); });
            Assert.Equal(position.X, store.SingleState.X, 4); Assert.Equal(position.Y, store.SingleState.Z, 4);
            using var duplicate = new NetworkMovementIntegrationTests.TestClient(port, token);
            await Poll(duplicate, () => duplicate.Rejection.HasValue);
            Assert.Equal(HandshakeRejectCode.CharacterInUse, duplicate.Rejection!.Value.Code);
            Assert.Empty(duplicate.Spawns);
            first.Disconnect();
            await Poll(first, () => !store.IsHeld);
            using var reconnect = new NetworkMovementIntegrationTests.TestClient(port, token);
            await Poll(reconnect, () => reconnect.Spawns.Count != 0);
            Assert.Equal(position, reconnect.LocalSpawn.Position);
            Assert.NotEqual(firstEntity, reconnect.LocalSpawn.EntityId);
            Assert.Equal(character, store.SingleId);
            await server.StopAsync(CancellationToken.None);
        }
        store = new SqliteCharacterStore(store.DatabasePath);
        using (var server = Server(port, store))
        {
            await server.StartAsync(CancellationToken.None);
            using var restarted = new NetworkMovementIntegrationTests.TestClient(port, token);
            await Poll(restarted, () => restarted.Spawns.Count != 0);
            Assert.Equal(position, restarted.LocalSpawn.Position);
            Assert.Equal(character, store.SingleId);
            using var invalid = new NetworkMovementIntegrationTests.TestClient(port, new string('A', 64));
            await Poll(invalid, () => invalid.Rejection.HasValue);
            Assert.Equal(HandshakeRejectCode.InvalidIdentity, invalid.Rejection!.Value.Code);
            Assert.Equal(1, store.Count);
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UncommittedMovementIsNotPublishedAndNetworkStillAcceptsConnections()
    {
        var store = new SqliteCharacterStore();
        var port = FreePort();
        using var server = Server(port, store);
        await server.StartAsync(CancellationToken.None);
        using var client = new NetworkMovementIntegrationTests.TestClient(port);
        await Poll(client, () => client.Spawns.Count != 0);
        var initial = client.LocalSpawn;
        var gate = store.PauseSaves();
        try
        {
            client.Move(1, new(-6, 2));
            await Poll(client, () => store.PendingSave);
            var committedX = store.SingleState.X;
            for (var i = 0; i < 12; i++) { client.Poll(); await Task.Delay(25); }
            Assert.Equal(initial.Position.X, committedX);
            Assert.All(client.States.Values.Where(state => state.EntityId == initial.EntityId),
                state => Assert.Equal(initial.Position, state.Position));
            using var duplicate = new NetworkMovementIntegrationTests.TestClient(port, client.Token);
            // Polling accepts the new peer/hello while the checkpoint is pending (no spin/blocking DB I/O).
            await Poll(duplicate, () => store.OpenAttempts >= 2);
            gate.TrySetResult();
            await Poll(client, () => client.IsAt(initial.EntityId, new(-6, 2)));
            Assert.Equal(-6, store.SingleState.X, 4);
        }
        finally { gate.TrySetResult(); await server.StopAsync(CancellationToken.None); }
    }

    internal static GameServerService Server(int port, ICharacterStore store) => new(
        Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
        new HandshakeCoordinator(), World(), NullLogger<GameServerService>.Instance, store);

    [Fact]
    public async Task FailedCheckpointStopsSimulationWithoutPublishingOrResettingIdentity()
    {
        var sqlite = new SqliteCharacterStore();
        var store = new FailingStore(sqlite);
        var port = FreePort();
        using var server = Server(port, store);
        await server.StartAsync(CancellationToken.None);
        using var client = new NetworkMovementIntegrationTests.TestClient(port);
        await Poll(client, () => client.Spawns.Count != 0);
        var initial = client.LocalSpawn;
        store.FailWrites = true;
        client.Move(1, new(-6, 2));
        await Poll(client, () => server.ExecuteTask!.IsCompleted);
        await Assert.ThrowsAnyAsync<Exception>(() => server.ExecuteTask!);
        Assert.Equal(initial.Position.X, sqlite.SingleState.X);
        Assert.All(client.States.Values.Where(state => state.EntityId == initial.EntityId),
            state => Assert.Equal(initial.Position, state.Position));
        Assert.False(sqlite.IsHeld);
        Assert.Equal(1, sqlite.Count);
        Assert.Equal(64, client.Token.Length);
        await server.StopAsync(CancellationToken.None);
    }

    private sealed class FailingStore(ICharacterStore inner) : ICharacterStore
    {
        internal volatile bool FailWrites;
        public Task InitializeAsync(CancellationToken token) => inner.InitializeAsync(token);
        public Task<CharacterSession> OpenAsync(string credential, CharacterState initial, CancellationToken token) => inner.OpenAsync(credential, initial, token);
        public Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken token) => FailWrites
            ? Task.FromException(new IOException("Injected durable-store failure.")) : inner.SaveAsync(changes, token);
    }

    internal static int FreePort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    internal static async Task Poll(NetworkMovementIntegrationTests.TestClient client, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested(); client.Poll();
            if (condition()) return;
            await Task.Delay(25, timeout.Token);
        }
    }
}
