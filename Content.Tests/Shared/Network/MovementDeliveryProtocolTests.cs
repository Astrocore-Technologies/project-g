using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class MovementDeliveryProtocolTests
{
    [Theory]
    [InlineData(MovementCommandKind.Cursor)] [InlineData(MovementCommandKind.Destination)] [InlineData(MovementCommandKind.Stop)]
    [InlineData(MovementCommandKind.CursorStart)]
    public void IntentKindRoundTripsAndRejectsMalformedLayout(MovementCommandKind kind)
    {
        var command = new MoveCommand(7, uint.MaxValue, new(4, 5), 6, 19, 9, kind);
        var bytes = NetworkProtocol.Write(command).CopyData();
        Assert.Equal(35, bytes.Length);
        Assert.True(NetworkProtocol.TryReadMoveCommand(Body(bytes), out var actual)); Assert.Equal(command, actual);
        for (var n = 2; n < bytes.Length; n++) Assert.False(NetworkProtocol.TryReadMoveCommand(Body(bytes[..n]), out _));
        Assert.False(NetworkProtocol.TryReadMoveCommand(Body([..bytes, 0]), out _));
        bytes[^5] = byte.MaxValue;
        Assert.False(NetworkProtocol.TryReadMoveCommand(Body(bytes), out _));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(command with { Kind = (MovementCommandKind)255 }));
    }

    [Fact]
    public void SparseInputAcknowledgementFitsTheActualRegionEnvelopeBudget()
    {
        var entity = new EntitySnapshot(new(1), Vector2.Zero, 8, Vector2.UnitY,
            CancelledNavigationOrder: 3, ProcessedClientTick: uint.MaxValue, DirectCursor: true, RunningIntent: true);
        var count = NetworkProtocol.SnapshotCapacity(1200 - NetworkProtocol.RegionEnvelopeBytes);
        Assert.Equal(16, count);
        var bytes = NetworkProtocol.Write(new WorldSnapshot(24,
            Enumerable.Range(1, count).Select(i => entity with { EntityId = new((ulong)i) }).ToArray())).CopyData();
        Assert.Equal(7 + 73 * count, bytes.Length);
        Assert.True(bytes.Length + NetworkProtocol.RegionEnvelopeBytes <= 1200);
        Assert.True(NetworkProtocol.TryReadWorldSnapshot(Body(bytes), out var snapshot));
        Assert.All(snapshot.Entities, state => { Assert.Equal(uint.MaxValue, state.ProcessedClientTick); Assert.True(state.DirectCursor && state.RunningIntent); });
        Assert.False(NetworkProtocol.TryReadWorldSnapshot(Body(bytes[..^1]), out _));
        Assert.False(NetworkProtocol.TryReadWorldSnapshot(Body([..bytes, 0]), out _));
    }

    private static NetDataReader Body(byte[] bytes)
    { var reader = new NetDataReader(bytes); Assert.True(NetworkProtocol.TryReadMessageType(reader, out _)); return reader; }
}
