using System.Numerics;
using Content.Server.World;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class SpatialIndexTests
{
    [Fact]
    public void QueryUsesCircleAndHandlesNegativeCellBoundaries()
    {
        var index = new SpatialIndex(4f);
        index.Add(new NetworkEntityId(1), new Vector2(-4f, -4f));
        index.Add(new NetworkEntityId(2), new Vector2(-2f, -4f));
        index.Add(new NetworkEntityId(3), new Vector2(-2f, -2f));
        index.Add(new NetworkEntityId(4), new Vector2(100f, 100f));
        var results = new HashSet<NetworkEntityId>();

        index.Query(new Vector2(-4f, -4f), 2f, results);

        Assert.Equal(2, results.Count);
        Assert.Contains(new NetworkEntityId(1), results);
        Assert.Contains(new NetworkEntityId(2), results);
    }

    [Fact]
    public void MoveAndRemoveCleanOldBucketsAndQueryResults()
    {
        var index = new SpatialIndex(4f);
        var id = new NetworkEntityId(1);
        var results = new HashSet<NetworkEntityId>();
        index.Add(id, new Vector2(-1f, 0f));
        index.Move(id, new Vector2(9f, 0f));

        Assert.Equal(1, index.CellCount);
        index.Query(Vector2.Zero, 2f, results);
        Assert.Empty(results);
        index.Query(new Vector2(9f, 0f), 0f, results);
        Assert.Contains(id, results);
        Assert.True(index.Remove(id));
        Assert.False(index.Remove(id));
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.CellCount);
        index.Query(new Vector2(9f, 0f), 1f, results);
        Assert.Empty(results);
    }

    [Fact]
    public void InvalidInputsAreRejectedWithoutMutation()
    {
        var index = new SpatialIndex(4f);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.Add(NetworkEntityId.Invalid, Vector2.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.Add(new NetworkEntityId(1), new Vector2(float.NaN, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.Query(Vector2.Zero, 1000f, new HashSet<NetworkEntityId>()));
        Assert.Equal(0, index.Count);
    }
}
