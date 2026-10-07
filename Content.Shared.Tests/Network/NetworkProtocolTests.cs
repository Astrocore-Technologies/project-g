using Content.Shared.Network;
using LiteNetLib.Utils;
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

    private static NetDataReader CreateReader(NetDataWriter writer) =>
        new(writer.CopyData());
}
