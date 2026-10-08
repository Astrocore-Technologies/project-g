using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared;

public sealed class RegionProtocolTests
{
    [Fact]
    public void EntryRoundTripsAndRejectsEveryTruncationAndTrailingByte()
    {
        var value = new RegionEnter(9, "outskirts", new(-14, 10), .65f);
        var bytes = NetworkProtocol.Write(value).CopyData();
        var reader = new NetDataReader(bytes);
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var type));
        Assert.Equal(NetworkMessageType.RegionEnter, type);
        Assert.True(NetworkProtocol.TryReadRegionEnter(reader, out var result)); Assert.Equal(value, result);
        for (var length = 2; length < bytes.Length; length++)
            Assert.False(NetworkProtocol.TryReadRegionEnter(new NetDataReader(bytes[2..length]), out _));
        Assert.False(NetworkProtocol.TryReadRegionEnter(new NetDataReader([.. bytes[2..], 0]), out _));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(value with { Epoch = 0 }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(value with { Exit = new(float.NaN, 0) }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(value with { Region = "../secret" }));
    }

    [Fact]
    public void EnvelopePreservesInnerValidationAndRejectsNestedOrControlMessages()
    {
        var command = new MoveCommand(3, 8, new(4, 5)); var envelope = new NetDataWriter();
        NetworkProtocol.WrapRegion(envelope, 2, NetworkProtocol.Write(command));
        var bytes = envelope.CopyData(); var reader = new NetDataReader(bytes);
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.True(NetworkProtocol.TryReadRegionHeader(reader, out var epoch, out var type));
        Assert.Equal(2UL, epoch); Assert.Equal(NetworkMessageType.MoveCommand, type);
        Assert.True(NetworkProtocol.TryReadMoveCommand(reader, out var actual)); Assert.Equal(command, actual);
        for (var length = 2; length < bytes.Length; length++)
        {
            var cut = new NetDataReader(bytes[2..length]);
            Assert.False(NetworkProtocol.TryReadRegionHeader(cut, out _, out _) && NetworkProtocol.TryReadMoveCommand(cut, out _));
        }
        var trailing = new NetDataReader([.. bytes[2..], 0]);
        Assert.True(NetworkProtocol.TryReadRegionHeader(trailing, out _, out _));
        Assert.False(NetworkProtocol.TryReadMoveCommand(trailing, out _));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.WrapRegion(new(), 3, envelope));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.WrapRegion(new(), 3, NetworkProtocol.Write(new ClientHello(24, "test", ""))));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.WrapRegion(new(), 0, NetworkProtocol.Write(command)));
        bytes[2] = 0; // Little-endian epoch 2 -> 0.
        Assert.False(NetworkProtocol.TryReadRegionHeader(new NetDataReader(bytes[2..]), out _, out _));
    }

    [Fact]
    public void OuterBudgetIncludesEpochAndUnknownInnerIdsFailClosed()
    {
        var payload = new NetDataWriter(); payload.Put((ushort)NetworkMessageType.MoveCommand);
        payload.Put(new byte[NetworkConstants.MaxGamePacketBytes - NetworkProtocol.RegionEnvelopeBytes - 2]);
        var envelope = new NetDataWriter(); NetworkProtocol.WrapRegion(envelope, 1, payload);
        Assert.Equal(NetworkConstants.MaxGamePacketBytes, envelope.Length);
        payload.Put((byte)0); Assert.Throws<ArgumentException>(() => NetworkProtocol.WrapRegion(envelope, 1, payload));
        Assert.False(NetworkProtocol.TryReadRegionHeader(new NetDataReader(new byte[1200]), out _, out _));
        var invalid = new NetDataWriter(); invalid.Put(1UL); invalid.Put(ushort.MaxValue);
        Assert.False(NetworkProtocol.TryReadRegionHeader(new NetDataReader(invalid.CopyData()), out _, out _));
        invalid.Reset(); invalid.Put(1UL); invalid.Put((ushort)NetworkMessageType.RegionEnter);
        Assert.False(NetworkProtocol.TryReadRegionHeader(new NetDataReader(invalid.CopyData()), out _, out _));
        Assert.InRange(NetworkProtocol.SnapshotCapacity(1200 - NetworkProtocol.RegionEnvelopeBytes) * 44 + 7 + NetworkProtocol.RegionEnvelopeBytes, 1, 1200);
    }
}
