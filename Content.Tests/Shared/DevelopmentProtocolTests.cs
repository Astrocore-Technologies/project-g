using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared;

public sealed class DevelopmentProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    {
        var reader = new NetDataReader(bytes); Assert.True(NetworkProtocol.TryReadMessageType(reader, out _)); return reader;
    }
    [Fact]
    public void ReviveAndCapabilitiesRoundTripAndRejectMalformedBodies()
    {
        var command = NetworkProtocol.Write(new DevelopmentReviveCommand(4)).CopyData();
        Assert.True(NetworkProtocol.TryReadDevelopmentRevive(Body(command), out var read)); Assert.Equal(4u, read.Sequence);
        for (var length = 2; length < command.Length; length++) Assert.False(NetworkProtocol.TryReadDevelopmentRevive(Body(command[..length]), out _));
        Assert.False(NetworkProtocol.TryReadDevelopmentRevive(Body([.. command, 0]), out _));
        Array.Clear(command, 2, 4); Assert.False(NetworkProtocol.TryReadDevelopmentRevive(Body(command), out _));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new DevelopmentReviveCommand(0)));
        var tools = NetworkProtocol.Write(new DevelopmentTools(true)).CopyData();
        Assert.True(NetworkProtocol.TryReadDevelopmentTools(Body(tools), out var capabilities)); Assert.True(capabilities.CanRevive);
        Assert.False(NetworkProtocol.TryReadDevelopmentTools(Body(tools[..2]), out _));
        Assert.False(NetworkProtocol.TryReadDevelopmentTools(Body([.. tools, 0]), out _));
        tools[^1] = 2; Assert.False(NetworkProtocol.TryReadDevelopmentTools(Body(tools), out _));
    }
}
