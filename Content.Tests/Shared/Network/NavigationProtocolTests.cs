using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class NavigationProtocolTests
{
    [Fact]
    public void NavigationRoundTripsWithinPacketBudget()
    {
        var cells = new byte[900];
        cells[42] = 1;
        var expected = new RegionNavigation(new Vector2(-15f), 1f, 0.45f, 30, 30, cells);
        var writer = NetworkProtocol.Write(expected);
        Assert.True(writer.Length <= NetworkConstants.MaxGamePacketBytes);
        var reader = new NetDataReader(writer.CopyData());
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var type));
        Assert.Equal(NetworkMessageType.RegionNavigation, type);
        Assert.True(NetworkProtocol.TryReadRegionNavigation(reader, out var actual));
        Assert.Equal(expected.Origin, actual.Origin);
        Assert.Equal(expected.CellSize, actual.CellSize);
        Assert.Equal(expected.AgentRadius, actual.AgentRadius);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(cells, actual.BlockedCells);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void NavigationRejectsTruncatedOrTrailingBody(int lengthChange)
    {
        var bytes = NetworkProtocol.Write(new RegionNavigation(Vector2.Zero, 1f, 0.45f, 2, 2, new byte[4])).CopyData();
        Array.Resize(ref bytes, bytes.Length + lengthChange);
        var reader = new NetDataReader(bytes);
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.False(NetworkProtocol.TryReadRegionNavigation(reader, out _));
    }

    [Theory]
    [InlineData(0, 0, 1f, 0.45f, 0)]
    [InlineData(65535, 65535, 1f, 0.45f, 0)]
    [InlineData(2, 2, 0f, 0.45f, 0)]
    [InlineData(2, 2, 1f, float.NaN, 0)]
    [InlineData(2, 2, 1f, 0.45f, 2)]
    public void NavigationRejectsInvalidDimensionsGeometryOrCells(
        ushort width, ushort height, float cellSize, float radius, byte cellValue)
    {
        var writer = new NetDataWriter();
        writer.Put((ushort) NetworkMessageType.RegionNavigation);
        writer.Put(0f);
        writer.Put(0f);
        writer.Put(cellSize);
        writer.Put(radius);
        writer.Put(width);
        writer.Put(height);
        for (var i = 0; i < 4; i++) writer.Put(cellValue);
        var reader = new NetDataReader(writer.CopyData());
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.False(NetworkProtocol.TryReadRegionNavigation(reader, out _));
    }
}
