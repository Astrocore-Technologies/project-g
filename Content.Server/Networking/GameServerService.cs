using Content.Server.Configuration;
using Content.Shared.Network;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Content.Server.Networking;

/// <summary>
/// Owns the UDP transport lifecycle. Gameplay simulation remains separate.
/// </summary>
public sealed class GameServerService : BackgroundService
{
    private readonly ServerOptions _options;
    private readonly HandshakeCoordinator _handshakes;
    private readonly ILogger<GameServerService> _logger;
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _server;

    public GameServerService(
        IOptions<ServerOptions> options,
        HandshakeCoordinator handshakes,
        ILogger<GameServerService> logger)
    {
        _options = options.Value;
        _handshakes = handshakes;
        _logger = logger;
        _server = new NetManager(_listener);

        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.NetworkErrorEvent += OnNetworkError;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_server.Start(_options.Port))
            throw new InvalidOperationException($"Failed to start UDP server on port {_options.Port}.");

        _logger.LogInformation(
            "Server started. Port={Port}, Protocol={ProtocolVersion}, TickRate={TickRate}",
            _options.Port,
            NetworkConstants.ProtocolVersion,
            _options.TickRate);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _server.PollEvents();
                await Task.Delay(_options.NetworkPollIntervalMilliseconds, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        finally
        {
            _server.Stop();
            _logger.LogInformation("Server stopped cleanly");
        }
    }

    public override void Dispose()
    {
        _listener.ConnectionRequestEvent -= OnConnectionRequest;
        _listener.PeerConnectedEvent -= OnPeerConnected;
        _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
        _listener.NetworkReceiveEvent -= OnNetworkReceive;
        _listener.NetworkErrorEvent -= OnNetworkError;
        _server.Stop();
        base.Dispose();
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(_options.ConnectionKey);
    }

    private void OnPeerConnected(NetPeer peer)
    {
        _handshakes.RegisterConnection(peer.Id);
        _logger.LogInformation(
            "Peer connected. ConnectionId={ConnectionId}, Address={Address}",
            peer.Id,
            peer.Address);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        var playerId = _handshakes.RemoveConnection(peer.Id);
        _logger.LogInformation(
            "Peer disconnected. ConnectionId={ConnectionId}, PlayerId={PlayerId}, Reason={Reason}",
            peer.Id,
            playerId.Value,
            disconnectInfo.Reason);
    }

    private void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        try
        {
            if (reader.AvailableBytes > NetworkConstants.MaxHandshakePacketBytes)
            {
                Reject(peer, HandshakeRejectCode.MalformedPacket, "Handshake packet is too large.");
                return;
            }

            if (!NetworkProtocol.TryReadMessageType(reader, out var messageType) ||
                messageType != NetworkMessageType.ClientHello)
            {
                Reject(peer, HandshakeRejectCode.UnexpectedMessage, "ClientHello was expected.");
                return;
            }

            if (!NetworkProtocol.TryReadClientHello(reader, out var hello))
            {
                Reject(peer, HandshakeRejectCode.MalformedPacket, "ClientHello is malformed.");
                return;
            }

            var decision = _handshakes.ProcessHello(peer.Id, hello);
            if (!decision.IsAccepted)
            {
                Reject(peer, decision.RejectCode, decision.RejectReason);
                return;
            }

            var welcome = new ServerWelcome(
                decision.PlayerId,
                checked((ushort) _options.TickRate),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            peer.Send(NetworkProtocol.Write(welcome), DeliveryMethod.ReliableOrdered);
            _logger.LogInformation(
                "Handshake accepted. ConnectionId={ConnectionId}, PlayerId={PlayerId}, Build={BuildVersion}",
                peer.Id,
                decision.PlayerId.Value,
                hello.BuildVersion);
        }
        finally
        {
            reader.Recycle();
        }
    }

    private void OnNetworkError(
        System.Net.IPEndPoint endpoint,
        System.Net.Sockets.SocketError error)
    {
        _logger.LogWarning(
            "Network error. Endpoint={Endpoint}, SocketError={SocketError}",
            endpoint,
            error);
    }

    private void Reject(NetPeer peer, HandshakeRejectCode code, string reason)
    {
        var rejection = NetworkProtocol.Write(new ServerReject(code, reason));

        _logger.LogWarning(
            "Handshake rejected. ConnectionId={ConnectionId}, Code={Code}, Reason={Reason}",
            peer.Id,
            code,
            reason);

        // The disconnect packet delivers the rejection atomically with connection close.
        _server.DisconnectPeer(peer, rejection);
    }
}
