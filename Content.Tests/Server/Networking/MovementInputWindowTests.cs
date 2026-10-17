using System.Numerics;
using Content.Server.Networking;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Networking;

public sealed class MovementInputWindowTests
{
    [Fact]
    public void RejectsReplaysBackwardsTimeFutureAndStaleTimestampsWithoutMovingItsClock()
    {
        var window = new MovementInputWindow(new(1), 20);
        Assert.True(window.Accept(new(1, 100, Vector2.Zero), 1000));
        Assert.False(window.Accept(new(1, 100, Vector2.Zero), 1050));
        Assert.False(window.Accept(new(2, 99, Vector2.Zero), 1100));
        Assert.False(window.Accept(new(2, 1000, Vector2.Zero), 1150));
        Assert.True(window.Accept(new(2, 104, Vector2.Zero), 1200));
        Assert.False(window.Accept(new(3, 104, Vector2.Zero), 13000));
        Assert.True(window.Accept(new(3, 341, Vector2.Zero), 13050));
    }

    [Fact]
    public void PacketFloodIsBoundedAndRecoversWithoutAcceptingADuplicate()
    {
        var window = new MovementInputWindow(new(1), 20);
        var accepted = 0;
        for (uint sequence = 1; sequence < 100; sequence++)
            if (window.Accept(new(sequence, 0, Vector2.Zero), 0)) accepted++;
        Assert.Equal(12, accepted);
        Assert.True(window.Accept(new(100, 1, Vector2.Zero), 50));
        Assert.False(window.Accept(new(100, 1, Vector2.Zero), 100));
    }

    [Fact]
    public void SequenceAndClientClockWrapAreIndependent()
    {
        var window = new MovementInputWindow(new(1), 20);
        Assert.True(window.Accept(new(uint.MaxValue, uint.MaxValue, Vector2.Zero), 0));
        Assert.True(window.Accept(new(1, 0, Vector2.Zero), 50));
        Assert.False(window.Accept(new(uint.MaxValue, uint.MaxValue, Vector2.Zero), 100));
    }

    [Theory]
    [InlineData(20, 20, 20)] [InlineData(30, 20, 20)] [InlineData(20, 5, 5)]
    [InlineData(60, 20, 20)] [InlineData(10, 20, 10)]
    public void SnapshotScheduleHasNoRoundingDrift(int ticks, int rate, int expected)
    {
        var schedule = new SnapshotSchedule(ticks, rate);
        var count = 0; var previous = 0; var gaps = new List<int>();
        for (var tick = 1; tick <= ticks * 60; tick++)
            if (schedule.Advance()) { count++; gaps.Add(tick - previous); previous = tick; }
        Assert.Equal(expected * 60, count);
        Assert.All(gaps, gap => Assert.InRange(gap, Math.Max(1, ticks / expected), (int)Math.Ceiling(ticks / (double)expected)));
    }
}
