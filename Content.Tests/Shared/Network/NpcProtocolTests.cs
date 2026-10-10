using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class NpcProtocolTests
{
    [Theory]
    [InlineData(NpcAreaPhase.Telegraph)] [InlineData(NpcAreaPhase.Impact)] [InlineData(NpcAreaPhase.Finished)]
    public void AreaRoundTripsAndRejectsTruncationTrailingEnumsAndNonFiniteData(NpcAreaPhase phase)
    {
        var value = new NpcArea(new(1), 2, 3, Vector2.One, 2, phase == NpcAreaPhase.Finished ? 0 : 1, phase);
        var bytes = NetworkProtocol.Write(value).CopyData();
        Assert.Equal(39, bytes.Length);
        Assert.True(NetworkProtocol.TryReadNpcArea(new(bytes, 2, bytes.Length), out var parsed)); Assert.Equal(value, parsed);
        for (var end = 2; end < bytes.Length; end++) Assert.False(NetworkProtocol.TryReadNpcArea(new(bytes, 2, end), out _));
        var extra = new byte[bytes.Length + 1]; bytes.CopyTo(extra, 0);
        Assert.False(NetworkProtocol.TryReadNpcArea(new(extra, 2, extra.Length), out _));
        bytes[34] = 255;
        Assert.False(NetworkProtocol.TryReadNpcArea(new(bytes, 2, bytes.Length), out _));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(value with { Center = new(float.NaN, 0) }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(value with { Radius = -1 }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(value with { Phase = NpcAreaPhase.Finished, RemainingSeconds = 1 }));
    }

    [Fact]
    public void WindupRoundTripsAndRejectsEveryTruncationAndTrailingData()
    {
        var value = new NpcWindup(new(1), 2, 3, Vector2.One, Vector2.UnitX, 2, 1);
        var bytes = NetworkProtocol.Write(value).CopyData();
        Assert.Equal(42, bytes.Length);
        Assert.True(NetworkProtocol.TryReadNpcWindup(new(bytes, 2, bytes.Length), out var parsed));
        Assert.Equal(value, parsed);
        for (var end = 2; end < bytes.Length; end++) Assert.False(NetworkProtocol.TryReadNpcWindup(new(bytes, 2, end), out _));
        var extra = new byte[bytes.Length + 1]; bytes.CopyTo(extra, 0);
        Assert.False(NetworkProtocol.TryReadNpcWindup(new(extra, 2, extra.Length), out _));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(value with { RemainingSeconds = float.NaN }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(value with { RemainingSeconds = -1 }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(value with { Direction = Vector2.Zero }));
        Assert.True(NetworkProtocol.TryReadNpcWindup(new(NetworkProtocol.Write(value with { RemainingSeconds = 0 }).CopyData(), 2, 42), out _));
    }
}
