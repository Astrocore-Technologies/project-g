using Content.Shared.Network;
using Content.Shared.Movement;
using LiteNetLib.Utils;
using System.Numerics;
using Xunit;

namespace Content.Shared.Tests.Network;

public sealed class NetworkProtocolTests
{
    [Fact]
    public void ClientHelloRoundTrips()
    {
        var expected = new ClientHello(NetworkConstants.ProtocolVersion, "1.2.3");
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.ClientHello, messageType);
        Assert.True(NetworkProtocol.TryReadClientHello(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ServerWelcomeRoundTrips()
    {
        var expected = new ServerWelcome(new PlayerId(42), 20, 123456789);
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.ServerWelcome, messageType);
        Assert.True(NetworkProtocol.TryReadServerWelcome(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ServerRejectRoundTrips()
    {
        var expected = new ServerReject(
            HandshakeRejectCode.UnsupportedProtocol,
            "Unsupported protocol.");
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.ServerReject, messageType);
        Assert.True(NetworkProtocol.TryReadServerReject(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TruncatedHelloIsRejected()
    {
        var writer = new NetDataWriter();
        writer.Put((ushort) NetworkMessageType.ClientHello);
        var reader = CreateReader(writer);

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.False(NetworkProtocol.TryReadClientHello(reader, out _));
    }

    [Fact]
    public void PlayerSpawnRoundTrips()
    {
        var expected = new PlayerSpawn(
            new PlayerId(3),
            new NetworkEntityId(7),
            new Vector2(1f, 2f),
            new MovementSettings(5f, 0.1f, -15f, 15f, -15f, 15f));
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.PlayerSpawn, messageType);
        Assert.True(NetworkProtocol.TryReadPlayerSpawn(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MoveCommandRoundTrips()
    {
        var expected = new MoveCommand(12, 20, new Vector2(4f, -3f));
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.MoveCommand, messageType);
        Assert.True(NetworkProtocol.TryReadMoveCommand(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WorldSnapshotRoundTrips()
    {
        var entity = new EntitySnapshot(
            new NetworkEntityId(9),
            new Vector2(2f, 3f),
            14);
        var reader = CreateReader(NetworkProtocol.Write(
            new WorldSnapshot(22, new[] { entity })));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.WorldSnapshot, messageType);
        Assert.True(NetworkProtocol.TryReadWorldSnapshot(reader, out var actual));
        Assert.Equal((uint) 22, actual.ServerTick);
        Assert.Single(actual.Entities);
        Assert.Equal(entity, actual.Entities[0]);
    }

    private static NetDataReader CreateReader(NetDataWriter writer) =>
        new(writer.CopyData());
}
