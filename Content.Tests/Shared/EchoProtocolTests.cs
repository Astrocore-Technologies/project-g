using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared;

public sealed class EchoProtocolTests
{
    private delegate bool Read(NetDataReader reader);
    private static NetDataReader Body(byte[] bytes)
    { var reader = new NetDataReader(bytes); Assert.True(NetworkProtocol.TryReadMessageType(reader, out _)); return reader; }
    private static void Check(byte[] bytes, Read read)
    {
        Assert.True(read(Body(bytes)));
        for (var length = 2; length < bytes.Length; length++) Assert.False(read(Body(bytes[..length])));
        Assert.False(read(Body([.. bytes, 0])));
    }
    [Fact]
    public void AllMessagesRoundTripAndRejectTruncationAndTrailingBytes()
    {
        var spawn = new EchoSpawn(new(2), new(1), 1, 12, new(3, 4), "Мира");
        Check(NetworkProtocol.Write(spawn).CopyData(), reader => NetworkProtocol.TryReadEchoSpawn(reader, out _));
        Assert.True(NetworkProtocol.TryReadEchoSpawn(Body(NetworkProtocol.Write(spawn).CopyData()), out var parsed)); Assert.Equal(spawn, parsed);
        var command = new EchoSignatureCommand(1, 10, 1, new(3, 4));
        Check(NetworkProtocol.Write(command).CopyData(), reader => NetworkProtocol.TryReadEchoSignatureCommand(reader, out _));
        Assert.True(NetworkProtocol.TryReadEchoSignatureCommand(Body(NetworkProtocol.Write(command).CopyData()), out var parsedCommand)); Assert.Equal(command, parsedCommand);
        var result = new EchoSignatureResult(1, 12, 1, EchoCommandOutcome.Accepted, 8);
        Check(NetworkProtocol.Write(result).CopyData(), reader => NetworkProtocol.TryReadEchoSignatureResult(reader, out _));
        var action = new EchoAction(new(2), 12, EchoActionKind.Signature, new(3), new(3, 4), 2, 24, 100);
        Check(NetworkProtocol.Write(action).CopyData(), reader => NetworkProtocol.TryReadEchoAction(reader, out _));
        Assert.True(NetworkProtocol.TryReadEchoAction(Body(NetworkProtocol.Write(action).CopyData()), out var parsedAction)); Assert.Equal(action, parsedAction);
        var loadout = new EchoLoadout(new(1), 12, [new(1, new(2), 6, 2, 8), new(2, new(3), 6, 2, 0), new(3, new(4), 6, 2, 0)]);
        Check(NetworkProtocol.Write(loadout).CopyData(), reader => NetworkProtocol.TryReadEchoLoadout(reader, out _));
        Assert.True(NetworkProtocol.TryReadEchoLoadout(Body(NetworkProtocol.Write(loadout).CopyData()), out var parsedLoadout)); Assert.Equal(loadout.Slots, parsedLoadout.Slots);
    }
    [Fact]
    public void RejectsInvalidIdsSlotsEnumsNumbersAndDuplicateLoadout()
    {
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new EchoSpawn(new(1), new(1), 1, 0, default, "Мира")));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new EchoSignatureCommand(0, 0, 1, default)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new EchoSignatureCommand(1, 0, 4, default)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new EchoSignatureCommand(1, 0, 1, new(float.NaN, 0))));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new EchoLoadout(new(1), 0, [new(1, new(2), 6, 2, 0), new(1, new(3), 6, 2, 0)])));
        var command = NetworkProtocol.Write(new EchoSignatureCommand(1, 0, 1, default)).CopyData(); command[10] = 4;
        Assert.False(NetworkProtocol.TryReadEchoSignatureCommand(Body(command), out _));
        var result = NetworkProtocol.Write(new EchoSignatureResult(1, 0, 1, EchoCommandOutcome.Accepted, 0)).CopyData(); result[11] = 255;
        Assert.False(NetworkProtocol.TryReadEchoSignatureResult(Body(result), out _));
        var action = NetworkProtocol.Write(new EchoAction(new(1), 0, EchoActionKind.Signature, default, default, 2, 0, 0)).CopyData(); action[14] = 255;
        Assert.False(NetworkProtocol.TryReadEchoAction(Body(action), out _));
    }
}
