using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class IdentityProtocolTests
{
    [Fact]
    public void CredentialRoundTripsInsideBoundedHandshake()
    {
        var token = new string('F', 64);
        var hello = new ClientHello(NetworkConstants.ProtocolVersion, "dev", token);
        var packet = NetworkProtocol.Write(hello);
        Assert.True(packet.Length <= NetworkConstants.MaxHandshakePacketBytes);
        var reader = new NetDataReader(packet.CopyData());
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.True(NetworkProtocol.TryReadClientHello(reader, out var read));
        Assert.Equal(hello, read);
        var welcome = new ServerWelcome(new PlayerId(1), 20, 100, token);
        reader = new NetDataReader(NetworkProtocol.Write(welcome).CopyData());
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.True(NetworkProtocol.TryReadServerWelcome(reader, out var received));
        Assert.Equal(welcome, received);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void MalformedCredentialsCannotReachIdentityStore(string token)
    {
        var writer = new NetDataWriter();
        writer.Put((ushort)NetworkMessageType.ClientHello); writer.Put(NetworkConstants.ProtocolVersion);
        writer.Put("dev"); writer.Put(token);
        var reader = new NetDataReader(writer.CopyData());
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.False(NetworkProtocol.TryReadClientHello(reader, out _));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new ClientHello(NetworkConstants.ProtocolVersion, "dev", token)));
    }

    [Fact]
    public void TruncatedAndTrailingCredentialsAreRejected()
    {
        var data = NetworkProtocol.Write(new ClientHello(NetworkConstants.ProtocolVersion, "dev", new string('A', 64))).CopyData();
        for (var length = 2; length < data.Length; length++)
        {
            var reader = new NetDataReader(data[..length]);
            Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
            Assert.False(NetworkProtocol.TryReadClientHello(reader, out _));
        }
        var trailing = new NetDataReader(data.Concat(new byte[] { 0 }).ToArray());
        Assert.True(NetworkProtocol.TryReadMessageType(trailing, out _));
        Assert.False(NetworkProtocol.TryReadClientHello(trailing, out _));
    }

    [Fact]
    public void PreviousHelloLayoutIsOnlyUsableForVersionRejection()
    {
        var writer = new NetDataWriter(); writer.Put((ushort)NetworkMessageType.ClientHello);
        writer.Put((ushort)8); writer.Put("old");
        var reader = new NetDataReader(writer.CopyData());
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.True(NetworkProtocol.TryReadClientHello(reader, out var hello));
        var coordinator = new Content.Server.Networking.HandshakeCoordinator(); coordinator.RegisterConnection(1);
        Assert.Equal(HandshakeRejectCode.UnsupportedProtocol, coordinator.ProcessHello(1, hello).RejectCode);
    }
}
