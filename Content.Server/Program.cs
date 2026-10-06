using Content.Shared.Network;
using LiteNetLib;
using LiteNetLib.Utils;

var listener = new EventBasedNetListener();
var server = new NetManager(listener);

var nextPlayerId = 1;

listener.ConnectionRequestEvent += request =>
{
    request.AcceptIfKey(NetworkConstants.ConnectionKey);
};

listener.PeerConnectedEvent += peer =>
{
    int playerId = nextPlayerId++;

    Console.WriteLine(
        $"Player connected! PlayerId={playerId}, Address={peer.Address}:{peer.Port}"
    );

    var writer = new NetDataWriter();

    writer.Put((byte)NetworkMessageType.AssignPlayerId);
    writer.Put(playerId);

    peer.Send(
        writer,
        DeliveryMethod.ReliableOrdered
    );

    Console.WriteLine(
        $"Sent PlayerId={playerId} to client."
    );
};

listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
{
    Console.WriteLine(
        $"Player disconnected: {peer.Address}:{peer.Port}"
    );
};

if (!server.Start(NetworkConstants.Port))
{
    Console.WriteLine("ERROR: Failed to start server.");
    return;
}

Console.WriteLine(
    $"Content.Server started on port {NetworkConstants.Port}"
);

Console.WriteLine("Waiting for players...");
Console.WriteLine("Press Ctrl+C to stop.");

while (true)
{
    server.PollEvents();
    Thread.Sleep(15);
}