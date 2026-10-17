using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using LiteNetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client = Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class MovementDeliveryIntegrationTests
{
    [Fact]
    public async Task OneReliableDestinationRunsWithoutHeartbeatsAndReliableStopRejectsLateCursor()
    {
        var world = CreateWorld(); var port = CharacterPersistenceTests.FreePort();
        using var server = CreateServer(world, port);
        await server.StartAsync(default);
        using var client = new Client(port); // 100–150 ms + 10% loss, including reliable retransmission.
        try
        {
            await Poll(client, () => client.Spawns.Count == 1);
            var spawn = client.LocalSpawn; var target = spawn.Position + Vector2.UnitY * 6;
            var destination = new MoveCommand(1, 10, target, InputOrder: 10, Kind: MovementCommandKind.Destination);
            client.SendGame(NetworkProtocol.Write(destination), DeliveryMethod.ReliableOrdered);
            await Poll(client, () => client.States.TryGetValue(spawn.EntityId, out var state) && Vector2.Distance(state.Position, spawn.Position) > .5f);
            var beforeStop = client.States[spawn.EntityId].Position;
            Assert.True(client.States[spawn.EntityId].ProcessedClientTick > destination.ClientTick);
            client.SendGame(NetworkProtocol.Write(new MoveCommand(2, 20, spawn.Position,
                InputOrder: 11, Kind: MovementCommandKind.Stop)), DeliveryMethod.ReliableOrdered);
            await Poll(client, () => client.States[spawn.EntityId].LastProcessedSequence == 2);
            var stopped = client.States[spawn.EntityId].Position;
            Assert.Equal(stopped, client.States[spawn.EntityId].Target);
            Assert.True(Vector2.Distance(stopped, beforeStop) < 2);
            client.SendGame(NetworkProtocol.Write(destination with { Kind = MovementCommandKind.Cursor }), DeliveryMethod.Sequenced);
            await Pump(client, 650);
            Assert.Equal(stopped, client.States[spawn.EntityId].Position);
            Assert.Equal(2u, client.States[spawn.EntityId].LastProcessedSequence);
        }
        finally { await server.StopAsync(default); }
    }

    [Fact]
    public async Task WrongDeliveryCannotApplyDestinationAndSlowSnapshotsDoNotDelayCombatResults()
    {
        var world = CreateWorld(catalog: true); var port = CharacterPersistenceTests.FreePort();
        using var server = CreateServer(world, port, 1);
        await server.StartAsync(default);
        using var client = new Client(port, lossy: false);
        try
        {
            var snapshots = 0;
            client.SnapshotObserved = _ => snapshots++;
            await Poll(client, () => client.Spawns.Count == 1 && snapshots > 1);
            var spawn = client.LocalSpawn;
            client.SendGame(NetworkProtocol.Write(new MoveCommand(1, 0, spawn.Position + Vector2.UnitY * 3,
                InputOrder: 10, Kind: MovementCommandKind.Destination)), DeliveryMethod.Sequenced);
            await Pump(client, 100);
            Assert.Equal(spawn.Position, world.GetPlayer(0).Position);
            var priorSnapshots = snapshots;
            client.SendGame(NetworkProtocol.Write(new AttackCommand(1, 0, Vector2.UnitY, InputOrder: 11)), DeliveryMethod.ReliableOrdered);
            await Poll(client, () => client.Results.Count != 0);
            Assert.Equal(AttackOutcome.Accepted, client.Results[0].Outcome);
            Assert.Equal(priorSnapshots, snapshots);
        }
        finally { await server.StopAsync(default); }
    }

    [Fact]
    public async Task DisconnectStopsTheRouteAndReattachedSessionStartsWithFreshInputClock()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        var world = CreateWorld(catalog: true); var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new(), world, NullLogger<GameServerService>.Instance, store);
        await server.StartAsync(default);
        using var client = new Client(port, lossy: false);
        try
        {
            await Poll(client, () => client.Spawns.Count == 1);
            var spawn = client.LocalSpawn;
            client.SendGame(NetworkProtocol.Write(new MoveCommand(90, 100, spawn.Position + Vector2.UnitY * 5,
                InputOrder: 100, Kind: MovementCommandKind.Destination)), DeliveryMethod.ReliableOrdered);
            await Poll(client, () => client.States.TryGetValue(spawn.EntityId, out var state) && state.LastProcessedSequence == 90);
            client.Disconnect(); await Poll(client, () => server.DetachedCombatantCount == 1);
            using var rejoined = new Client(port, client.Token, lossy: false);
            await Poll(rejoined, () => rejoined.Spawns.Count == 1);
            Assert.Equal(spawn.EntityId, rejoined.LocalSpawn.EntityId);
            var stopped = rejoined.LocalSpawn.Position;
            await Pump(rejoined, 300);
            Assert.Equal(stopped, rejoined.States[spawn.EntityId].Position);
            rejoined.SendGame(NetworkProtocol.Write(new MoveCommand(1, 0, stopped - Vector2.UnitY,
                InputOrder: 1, Kind: MovementCommandKind.Destination)), DeliveryMethod.ReliableOrdered);
            await Poll(rejoined, () => rejoined.States[spawn.EntityId].LastProcessedSequence == 1);
            Assert.NotEqual(stopped, rejoined.States[spawn.EntityId].Target);
        }
        finally { await server.StopAsync(default); }
    }

    private static ServerWorld CreateWorld(bool catalog = false) => new(Options.Create(new MovementOptions()),
        Options.Create(new InterestOptions()), catalog: catalog ? ContentCatalogTests.Load() : null, hitRoll: () => 0, criticalRoll: () => 1);
    private static GameServerService CreateServer(ServerWorld world, int port, int snapshots = 20) => new(
        Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1, MovementSnapshotRate = snapshots }),
        new(), world, NullLogger<GameServerService>.Instance);
    private static async Task Poll(Client client, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        do { client.Poll(); if (condition()) return; await Task.Delay(5, timeout.Token); } while (true);
    }
    private static async Task Pump(Client client, int milliseconds)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds) { client.Poll(); await Task.Delay(5); }
    }
}
