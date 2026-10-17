using System.Numerics;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class CursorSteeringTests
{
    private static readonly MovementSettings Settings = new(5, .1f, -10, 10, -10, 10);

    [Theory] [InlineData(false)] [InlineData(true)]
    public void CursorHitsTheWallWithoutDetouringAndKeepsItsMovementIntent(bool surface)
    {
        var grid = Grid(surface); var start = new Vector2(-5, 0);
        var mover = new NavigationMover(grid, Settings, new(grid), start, surface ? -.5f : 0);
        Assert.True(mover.TrySetCursor(new(5, 0)));
        for (var tick = 0; tick < 100; tick++)
        {
            var before = mover.Foot; mover.Step(.05f);
            Assert.True(grid.IsOnSurface(mover.Foot)); Assert.True(grid.TraverseSurface(before, mover.Foot));
            Assert.InRange(MathF.Abs(mover.Position.Y), 0, .001f);
            Assert.InRange(Vector3.Distance(before, mover.Foot), 0, .25001f);
        }
        Assert.InRange(mover.Position.X, -1.6f, -1);
        Assert.True(mover.IsCursor && mover.IsMoving);
        Assert.Equal(NavigationMotionSegment.Stationary, mover.CurrentSegment());
        var blocked = mover.Foot; mover.Step(1); Assert.Equal(blocked, mover.Foot);
        Assert.True(mover.TrySetCursor(new(-5, 0))); mover.Step(.05f);
        Assert.True(mover.Position.X < blocked.X);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DestinationStillFindsARouteAroundTheSameWall(bool surface)
    {
        var grid = Grid(surface); var mover = new NavigationMover(grid, Settings, new(grid), new(-5, 0), surface ? -.5f : 0);
        Assert.True(mover.TrySetTarget(new(5, 0), surface ? .5f : 0));
        for (var tick = 0; tick < 120; tick++) mover.Step(.05f);
        Assert.Equal(new Vector2(5, 0), mover.Position); Assert.False(mover.IsCursor);
    }

    [Fact]
    public void SnapshotRestoreAndStopDoNotReplaceDirectSteeringWithAPath()
    {
        var grid = Grid(true); var mover = new NavigationMover(grid, Settings, new(grid), new(-5, 0), -.5f);
        Assert.True(mover.Restore(new(-2, 0), new(5, 0), default, 0, -.2f, -.2f, 0, directCursor: true));
        for (var tick = 0; tick < 40; tick++) mover.Step(.05f);
        Assert.InRange(mover.Position.X, -1.1f, -1);
        Assert.True(mover.Reset(mover.Position, mover.Position, mover.Height, mover.Height));
        Assert.False(mover.IsCursor); Assert.False(mover.IsMoving);
    }

    private static NavigationGrid Grid(bool surface)
    {
        var cells = new byte[400];
        for (var z = 7; z < 13; z++) for (var x = 9; x < 11; x++) cells[z * 20 + x] = 1;
        var grid = new NavigationGrid(new RegionNavigation(new(-10), 1, .45f, 20, 20, cells));
        if (!surface) return grid;
        var vertices = new List<SurfaceVertex>(); var polygons = new List<int[]>();
        for (var z = -10; z <= 10; z++) for (var x = -10; x <= 10; x++) vertices.Add(new(x, x * .1f, z));
        for (var z = 0; z < 20; z++) for (var x = 0; x < 20; x++)
        {
            if (x is >= 9 and < 11 && z is >= 7 and < 13) continue;
            var a = z * 21 + x; polygons.Add([a, a + 1, a + 22, a + 21]);
        }
        var map = new SurfaceGeometry { ScenePath = "res://Scenes/Regions/Tests/CursorSteering.tscn", SourceHash = new('A', 64),
            AgentRadius = .45f, Vertices = vertices.ToArray(), Polygons = polygons.ToArray() };
        map.Seal(); grid.AttachSurface(map); return grid;
    }
}
