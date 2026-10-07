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

public sealed class GameServerServiceTests
{
    [Fact]
    public async Task ServerCompletesHandshakeMovesPlayerAndStops()
    {
        var port = GetFreeUdpPort();
        var serverOptions = Options.Create(new ServerOptions
        {
            Port = port,
            TickRate = NetworkConstants.ServerTickRate,
            NetworkPollIntervalMilliseconds = 1,
            ConnectionKey = NetworkConstants.ConnectionKey
        });
        var world = new ServerWorld(
            Options.Create(new MovementOptions()), Options.Create(new InterestOptions()));

        using var server = new GameServerService(
            serverOptions,
            new HandshakeCoordinator(),
            world,
            NullLogger<GameServerService>.Instance);
        await server.StartAsync(CancellationToken.None);

        var listener = new EventBasedNetListener();
        var client = new NetManager(listener)
        {
            SimulateLatency = true,
            SimulationMinLatency = 100,
            SimulationMaxLatency = 150
        };
        var welcomeSource = NewSource<ServerWelcome>();
        var spawnSource = NewSource<PlayerSpawn>();
        var movedSource = NewSource<EntitySnapshot>();
        NetPeer? serverPeer = null;

        listener.PeerConnectedEvent += peer =>
        {
            serverPeer = peer;
            peer.Send(
                NetworkProtocol.Write(new ClientHello(
                    NetworkConstants.ProtocolVersion,
                    "integration-test")),
                DeliveryMethod.ReliableOrdered);
        };
        listener.NetworkReceiveEvent += (peer, reader, channel, deliveryMethod) =>
        {
            try
            {
                if (!NetworkProtocol.TryReadMessageType(reader, out var messageType))
                    return;

                switch (messageType)
                {
                    case NetworkMessageType.ServerWelcome
                        when NetworkProtocol.TryReadServerWelcome(reader, out var welcome):
                        welcomeSource.TrySetResult(welcome);
                        break;
                    case NetworkMessageType.PlayerSpawn
                        when NetworkProtocol.TryReadPlayerSpawn(reader, out var spawn):
                        spawnSource.TrySetResult(spawn);
                        break;
                    case NetworkMessageType.WorldSnapshot
                        when NetworkProtocol.TryReadWorldSnapshot(reader, out var snapshot):
                        if (spawnSource.Task.IsCompletedSuccessfully)
                        {
                            var entityId = spawnSource.Task.Result.EntityId;
                            var state = snapshot.Entities.FirstOrDefault(value => value.EntityId == entityId);
                            if (state.LastProcessedSequence == 1)
                                movedSource.TrySetResult(state);
                        }
                        break;
                }
            }
            finally
            {
                reader.Recycle();
            }
        };

        try
        {
            Assert.True(client.Start());
            client.Connect(IPAddress.Loopback.ToString(), port, NetworkConstants.ConnectionKey);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await PollUntilAsync(client, () => spawnSource.Task.IsCompleted, timeout.Token);

            var welcome = await welcomeSource.Task;
            var spawn = await spawnSource.Task;
            Assert.True(welcome.PlayerId.IsValid);
            Assert.Equal(welcome.PlayerId, spawn.PlayerId);

            var target = new Vector2(spawn.Position.X + 5f, spawn.Position.Y);
            serverPeer!.Send(
                NetworkProtocol.Write(new MoveCommand(1, 1, target)),
                DeliveryMethod.Sequenced);

            await PollUntilAsync(client, () => movedSource.Task.IsCompleted, timeout.Token);
            var moved = await movedSource.Task;

            Assert.Equal((uint) 1, moved.LastProcessedSequence);
            Assert.True(moved.Position.X > spawn.Position.X);
        }
        finally
        {
            client.Stop();
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    private static async Task PollUntilAsync(
        NetManager client,
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            client.PollEvents();
            await Task.Delay(5, cancellationToken);
        }
    }

    private static TaskCompletionSource<T> NewSource<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int GetFreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint) socket.Client.LocalEndPoint!).Port;
    }
}
