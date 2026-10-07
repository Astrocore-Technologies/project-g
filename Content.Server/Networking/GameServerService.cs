using System.Diagnostics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Content.Server.Networking;

/// <summary>
/// Owns transport and advances the authoritative world on a fixed tick.
/// All callbacks and simulation updates execute on this service's single thread.
/// </summary>
public sealed class GameServerService : BackgroundService
{
    private readonly ServerOptions _options;
    private readonly HandshakeCoordinator _handshakes;
    private readonly ServerWorld _world;
    private readonly ILogger<GameServerService> _logger;
    private readonly EventBasedNetListener _listener = new();
    private readonly Dictionary<int, NetPeer> _peers = new();
    private readonly NetManager _server;

    public GameServerService(
        IOptions<ServerOptions> options,
        HandshakeCoordinator handshakes,
        ServerWorld world,
        ILogger<GameServerService> logger)
    {
        _options = options.Value;
        _handshakes = handshakes;
        _world = world;
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

        var fixedDelta = 1f / _options.TickRate;
        var tickDuration = TimeSpan.FromSeconds(fixedDelta);
        var clock = Stopwatch.StartNew();
        var nextTick = clock.Elapsed + tickDuration;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _server.PollEvents();

                // Limit catch-up work so a temporary stall cannot spiral indefinitely.
                for (var catchUp = 0; clock.Elapsed >= nextTick && catchUp < 4; catchUp++)
                {
                    _world.Simulate(fixedDelta);
                    BroadcastSnapshot();
                    nextTick += tickDuration;
                }

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

    private void OnConnectionRequest(ConnectionRequest request) =>
        request.AcceptIfKey(_options.ConnectionKey);

    private void OnPeerConnected(NetPeer peer)
    {
        _peers[peer.Id] = peer;
        _handshakes.RegisterConnection(peer.Id);
        _logger.LogInformation(
            "Peer connected. ConnectionId={ConnectionId}, Address={Address}",
            peer.Id,
            peer.Address);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _peers.Remove(peer.Id);
        var playerId = _handshakes.RemoveConnection(peer.Id);
        var player = _world.RemovePlayer(peer.Id);

        if (player is not null)
            BroadcastReliable(NetworkProtocol.Write(new PlayerDespawn(player.EntityId)));

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
            if (reader.AvailableBytes > NetworkConstants.MaxGamePacketBytes ||
                !NetworkProtocol.TryReadMessageType(reader, out var messageType))
            {
                RejectMalformed(peer, "Packet is malformed or too large.");
                return;
            }

            if (!_handshakes.TryGetPlayerId(peer.Id, out _))
            {
                HandleHandshake(peer, reader, messageType);
                return;
            }

            if (messageType != NetworkMessageType.MoveCommand ||
                !NetworkProtocol.TryReadMoveCommand(reader, out var command))
            {
                _logger.LogWarning(
                    "Invalid game command. ConnectionId={ConnectionId}, Type={MessageType}",
                    peer.Id,
                    messageType);
                return;
            }

            _world.TryApplyMove(peer.Id, command);
        }
        finally
        {
            reader.Recycle();
        }
    }

    private void HandleHandshake(
        NetPeer peer,
        NetDataReader reader,
        NetworkMessageType messageType)
    {
        if (messageType != NetworkMessageType.ClientHello)
        {
            Reject(peer, HandshakeRejectCode.UnexpectedMessage, "ClientHello was expected.");
            return;
        }

        if (!NetworkProtocol.TryReadClientHello(reader, out var hello))
        {
            RejectMalformed(peer, "ClientHello is malformed.");
            return;
        }

        var decision = _handshakes.ProcessHello(peer.Id, hello);
        if (!decision.IsAccepted)
        {
            Reject(peer, decision.RejectCode, decision.RejectReason);
            return;
        }

        var player = _world.AddPlayer(peer.Id, decision.PlayerId);
        var welcome = new ServerWelcome(
            decision.PlayerId,
            checked((ushort) _options.TickRate),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        peer.Send(NetworkProtocol.Write(welcome), DeliveryMethod.ReliableOrdered);

        // The new client receives the complete current player set in reliable order.
        foreach (var currentPlayer in _world.Players)
            peer.Send(NetworkProtocol.Write(_world.CreateSpawn(currentPlayer)), DeliveryMethod.ReliableOrdered);

        // Existing clients only need the newly created player.
        foreach (var (connectionId, currentPeer) in _peers)
        {
            if (connectionId != peer.Id && _handshakes.TryGetPlayerId(connectionId, out _))
            {
                currentPeer.Send(
                    NetworkProtocol.Write(_world.CreateSpawn(player)),
                    DeliveryMethod.ReliableOrdered);
            }
        }

        _logger.LogInformation(
            "Handshake accepted. ConnectionId={ConnectionId}, PlayerId={PlayerId}, EntityId={EntityId}, Build={BuildVersion}",
            peer.Id,
            decision.PlayerId.Value,
            player.EntityId.Value,
            hello.BuildVersion);
    }

    private void BroadcastSnapshot()
    {
        if (_world.Players.Count == 0)
            return;

        var packet = NetworkProtocol.Write(_world.CreateSnapshot());
        foreach (var (connectionId, peer) in _peers)
        {
            if (_handshakes.TryGetPlayerId(connectionId, out _))
                peer.Send(packet, DeliveryMethod.Sequenced);
        }
    }

    private void BroadcastReliable(NetDataWriter packet)
    {
        foreach (var (connectionId, peer) in _peers)
        {
            if (_handshakes.TryGetPlayerId(connectionId, out _))
                peer.Send(packet, DeliveryMethod.ReliableOrdered);
        }
    }

    private void RejectMalformed(NetPeer peer, string reason) =>
        Reject(peer, HandshakeRejectCode.MalformedPacket, reason);

    private void Reject(NetPeer peer, HandshakeRejectCode code, string reason)
    {
        _logger.LogWarning(
            "Connection rejected. ConnectionId={ConnectionId}, Code={Code}, Reason={Reason}",
            peer.Id,
            code,
            reason);

        _server.DisconnectPeer(peer, NetworkProtocol.Write(new ServerReject(code, reason)));
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
}
