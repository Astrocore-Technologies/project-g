using System.Net;
using System.Net.Sockets;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using LiteNetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Server.Tests.Networking;

public sealed class GameServerServiceTests
{
    [Fact]
    public async Task ServerStartsCompletesHandshakeAndStops()
    {
        var port = GetFreeUdpPort();
        var options = Options.Create(new ServerOptions
        {
            Port = port,
            TickRate = NetworkConstants.ServerTickRate,
            NetworkPollIntervalMilliseconds = 1,
            ConnectionKey = NetworkConstants.ConnectionKey
        });

        using var server = new GameServerService(
            options,
            new HandshakeCoordinator(),
            NullLogger<GameServerService>.Instance);

        await server.StartAsync(CancellationToken.None);

        var listener = new EventBasedNetListener();
        var client = new NetManager(listener);
        var welcomeSource = new TaskCompletionSource<ServerWelcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        listener.PeerConnectedEvent += peer =>
        {
            var hello = new ClientHello(NetworkConstants.ProtocolVersion, "integration-test");
            peer.Send(NetworkProtocol.Write(hello), DeliveryMethod.ReliableOrdered);
        };
        listener.NetworkReceiveEvent += (peer, reader, channel, deliveryMethod) =>
        {
            try
            {
                if (NetworkProtocol.TryReadMessageType(reader, out var messageType) &&
                    messageType == NetworkMessageType.ServerWelcome &&
                    NetworkProtocol.TryReadServerWelcome(reader, out var welcome))
                {
                    welcomeSource.TrySetResult(welcome);
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

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!welcomeSource.Task.IsCompleted)
            {
                timeout.Token.ThrowIfCancellationRequested();
                client.PollEvents();
                await Task.Delay(5, timeout.Token);
            }

            var welcome = await welcomeSource.Task;
            Assert.True(welcome.PlayerId.IsValid);
            Assert.Equal(NetworkConstants.ServerTickRate, welcome.TickRate);
        }
        finally
        {
            client.Stop();
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    private static int GetFreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint) socket.Client.LocalEndPoint!).Port;
    }
}
