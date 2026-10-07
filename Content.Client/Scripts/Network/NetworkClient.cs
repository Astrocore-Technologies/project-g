using System.Reflection;
using Content.Shared.Network;
using Godot;
using LiteNetLib;
using LiteNetLib.Utils;

namespace ProjectG.Networking;

public partial class NetworkClient : Node
{
    private EventBasedNetListener _listener = null!;
    private NetManager _client = null!;
    private bool _handshakeComplete;

    public PlayerId LocalPlayerId { get; private set; } = PlayerId.Invalid;

    public override void _Ready()
    {
        _listener = new EventBasedNetListener();
        _client = new NetManager(_listener);

        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.NetworkErrorEvent += OnNetworkError;

        if (!_client.Start())
        {
            GD.PushError("Failed to start the network client.");
            return;
        }

        GD.Print("Connecting to server...");
        _client.Connect(
            "127.0.0.1",
            NetworkConstants.Port,
            NetworkConstants.ConnectionKey);
    }

    public override void _Process(double delta)
    {
        _client?.PollEvents();
    }

    public override void _ExitTree()
    {
        if (_listener is not null)
        {
            _listener.PeerConnectedEvent -= OnPeerConnected;
            _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
            _listener.NetworkReceiveEvent -= OnNetworkReceive;
            _listener.NetworkErrorEvent -= OnNetworkError;
        }

        _client?.Stop();
    }

    private void OnPeerConnected(NetPeer peer)
    {
        var buildVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ??
            "unknown";

        var hello = new ClientHello(NetworkConstants.ProtocolVersion, buildVersion);
        peer.Send(NetworkProtocol.Write(hello), DeliveryMethod.ReliableOrdered);
        GD.Print("Connected. Sending protocol handshake...");
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _handshakeComplete = false;
        LocalPlayerId = PlayerId.Invalid;

        if (TryReadDisconnectRejection(disconnectInfo, out var rejection))
        {
            GD.PushError(
                $"Server rejected the connection ({rejection.Code}): {rejection.Reason}");
            return;
        }

        GD.Print($"Disconnected from server: {disconnectInfo.Reason}");
    }

    private void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        try
        {
            if (reader.AvailableBytes > NetworkConstants.MaxHandshakePacketBytes ||
                !NetworkProtocol.TryReadMessageType(reader, out var messageType))
            {
                GD.PushError("Received a malformed handshake packet.");
                _client.DisconnectPeer(peer);
                return;
            }

            switch (messageType)
            {
                case NetworkMessageType.ServerWelcome:
                    HandleWelcome(peer, reader);
                    break;

                case NetworkMessageType.ServerReject:
                    HandleReject(peer, reader);
                    break;

                default:
                    GD.PushError($"Unexpected server message: {messageType}.");
                    _client.DisconnectPeer(peer);
                    break;
            }
        }
        finally
        {
            reader.Recycle();
        }
    }

    private void HandleWelcome(NetPeer peer, NetDataReader reader)
    {
        if (_handshakeComplete ||
            !NetworkProtocol.TryReadServerWelcome(reader, out var welcome))
        {
            GD.PushError("Received an invalid ServerWelcome message.");
            _client.DisconnectPeer(peer);
            return;
        }

        _handshakeComplete = true;
        LocalPlayerId = welcome.PlayerId;

        GD.Print(
            $"Handshake complete. PlayerId={LocalPlayerId.Value}, " +
            $"ServerTickRate={welcome.TickRate}.");
    }

    private void HandleReject(NetPeer peer, NetDataReader reader)
    {
        if (NetworkProtocol.TryReadServerReject(reader, out var rejection))
        {
            GD.PushError(
                $"Server rejected the connection ({rejection.Code}): {rejection.Reason}");
        }
        else
        {
            GD.PushError("Server rejected the connection with malformed details.");
        }

        _client.DisconnectPeer(peer);
    }

    private static bool TryReadDisconnectRejection(
        DisconnectInfo disconnectInfo,
        out ServerReject rejection)
    {
        rejection = default;
        var reader = disconnectInfo.AdditionalData;

        if (reader.AvailableBytes <= 0 ||
            reader.AvailableBytes > NetworkConstants.MaxHandshakePacketBytes ||
            !NetworkProtocol.TryReadMessageType(reader, out var messageType) ||
            messageType != NetworkMessageType.ServerReject)
        {
            return false;
        }

        return NetworkProtocol.TryReadServerReject(reader, out rejection);
    }

    private static void OnNetworkError(
        System.Net.IPEndPoint endpoint,
        System.Net.Sockets.SocketError error)
    {
        GD.PushWarning($"Network error from {endpoint}: {error}.");
    }
}
