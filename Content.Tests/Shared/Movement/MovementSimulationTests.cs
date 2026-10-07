using System.Numerics;
using Content.Shared.Movement;
using Xunit;

namespace Content.Tests.Shared.Movement;

public sealed class MovementSimulationTests
{
    private static readonly MovementSettings Settings =
        new(5f, 0.1f, -15f, 15f, -15f, 15f);

    [Fact]
    public void StepCannotExceedConfiguredSpeed()
    {
        var result = MovementSimulation.Step(Vector2.Zero, new Vector2(10f, 0f), Settings, 0.05f);
        Assert.Equal(0.25f, result.X, 4);
        Assert.Equal(0f, result.Y);
    }

    [Fact]
    public void InvalidTargetDoesNotMoveEntity()
    {
        var start = new Vector2(2f, 3f);
        var result = MovementSimulation.Step(
            start,
            new Vector2(float.NaN, 0f),
            Settings,
            0.05f);
        Assert.Equal(start, result);
    }

    [Fact]
    public void SequenceComparisonRejectsOldAndDuplicateValues()
    {
        Assert.True(MovementSimulation.IsSequenceNewer(11, 10));
        Assert.False(MovementSimulation.IsSequenceNewer(10, 10));
        Assert.False(MovementSimulation.IsSequenceNewer(9, 10));
        Assert.True(MovementSimulation.IsSequenceNewer(0, uint.MaxValue));
    }
}
