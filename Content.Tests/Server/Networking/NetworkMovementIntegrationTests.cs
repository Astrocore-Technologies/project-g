using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using LiteNetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Networking;

public sealed class NetworkMovementIntegrationTests
{
    [Fact]
    public async Task TwoClientsObserveMovementDisconnectAndReconnectOnLossyConnections()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint) socket.Client.LocalEndPoint!).Port;
        socket.Close();

        using var server = new GameServerService(
            Options.Create(new ServerOptions
            {
                Port = port,
                TickRate = NetworkConstants.ServerTickRate,
                NetworkPollIntervalMilliseconds = 1,
                ConnectionKey = NetworkConstants.ConnectionKey
            }),
            new HandshakeCoordinator(),
            new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions())),
            NullLogger<GameServerService>.Instance);
        await server.StartAsync(CancellationToken.None);

        using var first = new TestClient(port);
        using var second = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(first, second,
                () => first.Spawns.Count == 2 && second.Spawns.Count == 2, timeout.Token);
            var firstSpawn = first.LocalSpawn;
            var secondSpawn = second.LocalSpawn;
            Assert.NotEqual(firstSpawn.EntityId, secondSpawn.EntityId);

            var firstTarget = new Vector2(2f, 3f);
            var secondTarget = new Vector2(-2f, -3f);
            uint sequence = 0;
            // Repeat intentions as the real client does: unreliable loss must not stop movement.
            await PollUntilAsync(first, second, () =>
            {
                first.Move(++sequence, firstTarget);
                second.Move(sequence, secondTarget);
                return first.IsAt(firstSpawn.EntityId, firstTarget) &&
                       first.IsAt(secondSpawn.EntityId, secondTarget) &&
                       second.IsAt(firstSpawn.EntityId, firstTarget) &&
                       second.IsAt(secondSpawn.EntityId, secondTarget);
            }, timeout.Token);

            first.Disconnect();
            await PollUntilAsync(first, second,
                () => !second.Spawns.ContainsKey(firstSpawn.EntityId), timeout.Token);

            using var reconnected = new TestClient(port);
            await PollUntilAsync(reconnected, second,
                () => reconnected.Spawns.Count == 2 && second.Spawns.Count == 2,
                timeout.Token);
            // Stage 1 reconnect creates a fresh runtime session, not a persistent character.
            Assert.NotEqual(firstSpawn.EntityId, reconnected.LocalSpawn.EntityId);
            Assert.NotEqual(firstSpawn.PlayerId, reconnected.LocalSpawn.PlayerId);
            Assert.False(second.Spawns.ContainsKey(firstSpawn.EntityId));
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    // The seeded entities act as stationary load-test actors; only the observer needs a UDP peer.
    [Fact]
    public async Task ObserverReceivesAll65EntitiesInIndependentSnapshotChunks()
    {
        var port = GetFreePort();
        var world = new ServerWorld(Options.Create(new MovementOptions()),
            Options.Create(new InterestOptions { Radius = 30f, ExitRadius = 32f }));
        for (var i = 1; i <= 64; i++)
            world.AddPlayer(1000 + i, new PlayerId((ulong) 1000 + (ulong) i));
        using var server = CreateServer(port, world);
        await server.StartAsync(CancellationToken.None);
        using var observer = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(observer, observer,
                () => observer.Spawns.Count == 65 && observer.HasFullSnapshotAtOneTick(65), timeout.Token);
            Assert.Equal(new NetworkEntityId(65), observer.LocalSpawn.EntityId);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    [Fact]
    public async Task AoiExitAndReentryProduceDespawnAndFreshSpawn()
    {
        var port = GetFreePort();
        using var server = CreateServer(port, new ServerWorld(
            Options.Create(new MovementOptions()),
            Options.Create(new InterestOptions { Radius = 4f, ExitRadius = 5f })));
        await server.StartAsync(CancellationToken.None);
        using var first = new TestClient(port);
        using var second = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(first, second,
                () => first.Spawns.Count == 2 && second.Spawns.Count == 2, timeout.Token);
            var original = second.LocalSpawn;
            var firstSpawn = first.LocalSpawn;
            var farTarget = new Vector2(firstSpawn.Position.X < 0f ? 12f : -12f, 0f);
            uint sequence = 0;
            await PollUntilAsync(first, second, () =>
            {
                second.Move(++sequence, farTarget);
                return first.Spawns.Count == 1 && second.Spawns.Count == 1;
            }, timeout.Token);
            Assert.DoesNotContain(original.EntityId, first.Spawns.Keys);
            await PollUntilAsync(first, second, () =>
            {
                second.Move(++sequence, original.Position);
                return first.Spawns.Count == 2 && second.Spawns.Count == 2;
            }, timeout.Token);
            var reentered = first.Spawns[original.EntityId];
            Assert.Equal(original.PlayerId, reentered.PlayerId);
            Assert.True(reentered.ServerTick > original.ServerTick);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    private static GameServerService CreateServer(int port, ServerWorld world) => new(
        Options.Create(new ServerOptions
        {
            Port = port,
            TickRate = NetworkConstants.ServerTickRate,
            NetworkPollIntervalMilliseconds = 1,
            ConnectionKey = NetworkConstants.ConnectionKey
        }), new HandshakeCoordinator(), world, NullLogger<GameServerService>.Instance);

    private static int GetFreePort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint) socket.Client.LocalEndPoint!).Port;
    }

    private static async Task PollUntilAsync(
        TestClient first, TestClient second, Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            first.Poll();
            second.Poll();
            if (condition())
                return;
            await Task.Delay(50, cancellationToken);
        }
    }

    private sealed class TestClient : IDisposable
    {
        private readonly NetManager _manager;
        private readonly Dictionary<NetworkEntityId, EntitySnapshot> _states = new();
        private readonly Dictionary<NetworkEntityId, uint> _ticks = new();
        private NetPeer? _peer;
        private PlayerId _playerId;

        public Dictionary<NetworkEntityId, PlayerSpawn> Spawns { get; } = new();
        public PlayerSpawn LocalSpawn => Spawns.Values.Single(spawn => spawn.PlayerId == _playerId);

        public TestClient(int port)
        {
            var listener = new EventBasedNetListener();
            _manager = new NetManager(listener)
            {
                SimulateLatency = true,
                SimulationMinLatency = 100,
                SimulationMaxLatency = 150,
                SimulatePacketLoss = true,
                SimulationPacketLossChance = 10
            };
            listener.PeerConnectedEvent += peer =>
            {
                _peer = peer;
                peer.Send(NetworkProtocol.Write(new ClientHello(
                    NetworkConstants.ProtocolVersion, "two-client-test")), DeliveryMethod.ReliableOrdered);
            };
            listener.NetworkReceiveEvent += (_, reader, _, _) =>
            {
                try
                {
                    Assert.True(NetworkProtocol.TryReadMessageType(reader, out var type));
                    switch (type)
                    {
                        case NetworkMessageType.ServerWelcome:
                            Assert.True(NetworkProtocol.TryReadServerWelcome(reader, out var welcome));
                            _playerId = welcome.PlayerId;
                            break;
                        case NetworkMessageType.PlayerSpawn:
                            Assert.True(NetworkProtocol.TryReadPlayerSpawn(reader, out var spawn));
                            Spawns[spawn.EntityId] = spawn;
                            _ticks[spawn.EntityId] = spawn.ServerTick;
                            _states.Remove(spawn.EntityId);
                            break;
                        case NetworkMessageType.PlayerDespawn:
                            Assert.True(NetworkProtocol.TryReadPlayerDespawn(reader, out var despawn));
                            Spawns.Remove(despawn.EntityId);
                            _states.Remove(despawn.EntityId);
                            _ticks.Remove(despawn.EntityId);
                            break;
                        case NetworkMessageType.WorldSnapshot:
                            Assert.True(NetworkProtocol.TryReadWorldSnapshot(reader, out var snapshot));
                            foreach (var state in snapshot.Entities)
                            {
                                if (_ticks.TryGetValue(state.EntityId, out var tick) &&
                                    Content.Shared.Movement.MovementSimulation.IsSequenceNewer(snapshot.ServerTick, tick))
                                {
                                    _ticks[state.EntityId] = snapshot.ServerTick;
                                    _states[state.EntityId] = state;
                                }
                            }
                            break;
                    }
                }
                finally
                {
                    reader.Recycle();
                }
            };
            Assert.True(_manager.Start());
            _manager.Connect(IPAddress.Loopback.ToString(), port, NetworkConstants.ConnectionKey);
        }

        public void Poll() => _manager.PollEvents();
        public bool HasFullSnapshotAtOneTick(int count) =>
            _states.Count == count && _ticks.Values.GroupBy(tick => tick).Any(group => group.Count() == count);
        public void Move(uint sequence, Vector2 target) => _peer?.Send(
            NetworkProtocol.Write(new MoveCommand(sequence, sequence, target)), DeliveryMethod.Sequenced);
        public bool IsAt(NetworkEntityId id, Vector2 target) =>
            _states.TryGetValue(id, out var state) && Vector2.Distance(state.Position, target) < 0.05f;
        public void Disconnect()
        {
            if (_peer is not null)
                _manager.DisconnectPeer(_peer);
        }
        public void Dispose() => _manager.Stop();
    }
}
