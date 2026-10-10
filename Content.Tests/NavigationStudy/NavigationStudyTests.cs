using System.Numerics;
using ProjectG.NavigationStudy;
using Xunit;

namespace Content.Tests.NavigationStudy;

public sealed class NavigationStudyTests
{
    private static StudyMap Fixture() => StudyMap.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "navigation-study.json"));

    [Theory]
    [InlineData("Hill")]
    [InlineData("Pit")]
    [InlineData("BridgeTop")]
    [InlineData("BridgeUnder")]
    [InlineData("BridgeClimb")]
    [InlineData("NarrowPassage")]
    [InlineData("Cliff")]
    [InlineData("LongRoute")]
    public void BothBackendsRespectAuthoredReachability(string name)
    {
        var map = Fixture(); var mesh = new StudyMesh(map);
        var scenario = map.Scenarios.Single(s => s.Name == name);
        var polygon = new PolygonStudy(mesh); var grid = new TiledGridStudy(mesh);
        var path = new List<Vector3>();
        foreach (var find in new Func<Vector3, Vector3, string, List<Vector3>, int, SearchStatus>[] { polygon.Find, grid.Find })
        {
            var status = find(scenario.Start.Vector, scenario.Goal.Vector, map.Revision, path, 20_000);
            Assert.Equal(scenario.Reachable, status == SearchStatus.Success);
            Assert.NotEqual(SearchStatus.InvalidPoint, status);
            if (!scenario.Reachable) { Assert.Empty(path); continue; }
            Assert.InRange(Vector3.Distance(path[0], scenario.Start.Vector), 0, .35f);
            Assert.InRange(Vector3.Distance(path[^1], scenario.Goal.Vector), 0, .35f);
            // Every segment must remain in the baked physical corridor, including its interpolated height.
            for (var segment = 1; segment < path.Count; segment++)
            for (var step = 1; step < 10; step++)
            {
                var sample = Vector3.Lerp(path[segment - 1], path[segment], step / 10f);
                Assert.True(mesh.TryLocate(sample, .025f, out _), $"{name}: route leaves the walkable surface at {sample}; segment {path[segment - 1]} -> {path[segment]}");
            }
            var first = path.ToArray();
            Assert.Equal(status, find(scenario.Start.Vector, scenario.Goal.Vector, map.Revision, path, 20_000));
            Assert.Equal(first, path);
            if (name == "NarrowPassage")
            {
                var crossed = false;
                for (var i = 1; i < path.Count; i++)
                {
                    var a = path[i - 1]; var b = path[i];
                    if (a.Z > 8 || b.Z < 8 || MathF.Abs(a.Z - b.Z) < .0001f) continue;
                    var crossing = Vector3.Lerp(a, b, (8 - a.Z) / (b.Z - a.Z));
                    Assert.InRange(crossing.X, -6.9f + map.AgentRadius, -5.1f - map.AgentRadius);
                    crossed = true;
                }
                Assert.True(crossed, "Route bypassed the authored narrow opening.");
            }
        }
    }

    [Fact]
    public void BridgeFloorsAreDistinctAndClimbUsesApproach()
    {
        var map = Fixture(); var mesh = new StudyMesh(map);
        Assert.True(mesh.TryLocate(new(6, 0, -6), .35f, out var below));
        Assert.True(mesh.TryLocate(new(6, 4, -6), .35f, out var above));
        Assert.NotEqual(below.Triangle, above.Triangle);
        Assert.False(mesh.Trace(below, above));
        var path = new List<Vector3>();
        Assert.Equal(SearchStatus.Success, new PolygonStudy(mesh).Find(below.Position, above.Position, map.Revision, path));
        Assert.Contains(path, p => p.X < -3); // Must reach the foot of the western approach ramp.
    }

    [Fact]
    public void CeilingBlocksAttackButOpenSlopeDoesNot()
    {
        var visibility = new StudyVisibility(Fixture());
        Assert.False(visibility.Clear(new(6, 1, -6), new(6, 5, -6)));
        Assert.True(visibility.Clear(new(-10, 1.5f, 2), new(-10, 5.5f, -11)));
        Assert.False(visibility.Clear(new(float.NaN, 0, 0), Vector3.Zero));
        Assert.False(visibility.Clear(Vector3.Zero, new(200, 0, 0)));
    }

    [Fact]
    public void RevisionAndSearchBudgetFailClosed()
    {
        var map = Fixture(); var mesh = new StudyMesh(map); var scenario = map.Scenarios.Single(s => s.Name == "LongRoute");
        var polygon = new PolygonStudy(mesh); var grid = new TiledGridStudy(mesh); var path = new List<Vector3>();
        foreach (var find in new Func<Vector3, Vector3, string, List<Vector3>, int, SearchStatus>[] { polygon.Find, grid.Find })
        {
            path.Add(Vector3.One);
            Assert.Equal(SearchStatus.StaleRevision, find(scenario.Start.Vector, scenario.Goal.Vector, "old", path, 20_000));
            Assert.Empty(path);
            Assert.Equal(SearchStatus.BudgetExceeded, find(scenario.Start.Vector, scenario.Goal.Vector, map.Revision, path, 1));
            Assert.Empty(path);
            Assert.Equal(SearchStatus.InvalidPoint, find(new(1000, 0, 1000), scenario.Goal.Vector, map.Revision, path, 20_000));
        }
    }

    [Fact]
    public void BlockingTheApproachSeparatesBridgeAndRejectsOldRevision()
    {
        var map = Fixture(); var mesh = new StudyMesh(map); var polygon = new PolygonStudy(mesh); var grid = new TiledGridStudy(mesh);
        var scenario = map.Scenarios.Single(s => s.Name == "BridgeClimb"); var path = new List<Vector3>();
        var ramp = mesh.Triangles.Select((t, id) => (t.Center, Id: id))
            .Where(t => t.Center.Y > .4f && t.Center.Y < 3.6f && MathF.Abs(t.Center.Z + 6) < 2 && MathF.Abs(t.Center.X) < 4)
            .Select(t => t.Id).ToArray();
        Assert.NotEmpty(ramp);
        mesh.ApplyPatch(ramp, true);
        foreach (var find in new Func<Vector3, Vector3, string, List<Vector3>, int, SearchStatus>[] { polygon.Find, grid.Find })
        {
            Assert.Equal(SearchStatus.StaleRevision, find(scenario.Start.Vector, scenario.Goal.Vector, map.Revision, path, 20_000));
            Assert.Equal(SearchStatus.Unreachable, find(scenario.Start.Vector, scenario.Goal.Vector, mesh.Revision, path, 20_000));
            Assert.Empty(path);
        }
        mesh.ApplyPatch(ramp, false);
        Assert.Equal(SearchStatus.Success, polygon.Find(scenario.Start.Vector, scenario.Goal.Vector, mesh.Revision, path));
        Assert.Equal(SearchStatus.Success, grid.Find(scenario.Start.Vector, scenario.Goal.Vector, mesh.Revision, path));
        var revision = mesh.Revision;
        Assert.Throws<InvalidDataException>(() => mesh.ApplyPatch([ramp[0], int.MaxValue], true));
        Assert.Equal(revision, mesh.Revision);
        Assert.False(mesh.Blocked[ramp[0]]);
    }

    [Fact]
    public void MalformedGeometryIsRejectedBeforeIndexing()
    {
        var map = Fixture(); map.Vertices[0] = new(float.NaN, 0, 0);
        Assert.Throws<InvalidDataException>(() => new StudyMesh(map));
        map = Fixture(); map.Polygons[0][0] = int.MaxValue;
        Assert.Throws<InvalidDataException>(() => new StudyMesh(map));
        map = Fixture(); map.Polygons = [map.Polygons[0], map.Polygons[0], map.Polygons[0]];
        Assert.Throws<InvalidDataException>(() => new StudyMesh(map));
    }
}
