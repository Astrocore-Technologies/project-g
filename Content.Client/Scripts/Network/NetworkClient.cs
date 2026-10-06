using Content.Shared.Network;
using Godot;
using LiteNetLib;

public partial class NetworkClient : Node
{
    private EventBasedNetListener _listener = null!;
    private NetManager _client = null!;

    public override void _Ready()
    {
        _listener = new EventBasedNetListener();
        _client = new NetManager(_listener);

        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;

        _client.Start();

        GD.Print("Connecting to server...");

        _client.Connect(
            "127.0.0.1",
            NetworkConstants.Port,
            NetworkConstants.ConnectionKey
        );
    }

    public override void _Process(double delta)
    {
        _client.PollEvents();
    }

    private void OnPeerConnected(NetPeer peer)
    {
        GD.Print("Connected to Content.Server!");
    }

    private void OnPeerDisconnected(
        NetPeer peer,
        DisconnectInfo disconnectInfo)
    {
        GD.Print("Disconnected from Content.Server.");
    }

    public override void _ExitTree()
    {
        _client?.Stop();
    }
}