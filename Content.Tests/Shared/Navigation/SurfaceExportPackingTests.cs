using Content.Shared.Navigation;
using ProjectG.NavigationStudy;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class SurfaceExportPackingTests
{
    [Fact]
    public void CompactionDropsOnlyUnreferencedVerticesAndKeepsFaceOrder()
    {
        int[][] faces = [[7,2,5],[5,2,9]];
        var compact = SurfaceExportPacking.CompactVertices(faces);
        Assert.Equal(new[]{2,5,7,9},compact.SourceVertices);
        Assert.Equal(new[]{2,0,1},compact.Polygons[0]);
        Assert.Equal(new[]{1,0,3},compact.Polygons[1]);
    }

    [Fact]
    public void ExportRemovesOnlyZeroFootprintFaces()
    {
        (float X, float Z)[] vertices = [(0,0),(1,1),(2,2),(2,0)];
        var packed = SurfaceExportPacking.RemoveDegenerateFaces(vertices, [[0,1,2,3]]);
        Assert.Single(packed);
        Assert.Equal(new[]{0,2,3}, packed[0]);
    }

    [Fact]
    public void PairingPreservesOriginalFacesOnNonPlanarTerrainAndSeparateFloors()
    {
        int[][] original = [[0,1,2],[0,2,3],[4,5,6],[7,8,9]];
        var packed = SurfaceExportPacking.PairTriangles(original);
        Assert.Equal(3, packed.Length);
        var faces = new List<string>();
        foreach (var polygon in packed)
            for (var i = 1; i+1 < polygon.Length; i++)
                faces.Add(string.Join(',', new[]{polygon[0],polygon[i],polygon[i+1]}.Order()));
        Assert.Equal(original.Select(p => string.Join(',',p.Order())).Order(), faces.Order());
        SurfaceVertex[] vertices = [new(0,0,0),new(10,1,0),new(10,4,10),new(0,2,10),
            new(0,8,0),new(10,8,0),new(10,8,10),new(20,0,20),new(30,0,20),new(20,0,30)];
        SurfaceNavigation Build(int[][] polygons)
        {
            var map = new SurfaceGeometry { ScenePath="res://Scenes/Regions/Test/Test.tscn",SourceHash=new string('A',64),Vertices=vertices,Polygons=polygons };
            map.Seal(); return new(map);
        }
        var before = Build(original); var after = Build(packed);
        foreach (var point in new[]{new System.Numerics.Vector3(8,2,2),new(2,2,8),new(8,8,2)})
        {
            var a = new List<SurfaceLocation>(); var b = new List<SurfaceLocation>();
            before.SampleColumn(point.X, point.Z, a); after.SampleColumn(point.X, point.Z, b);
            var first = a.Select(p=>p.Position.Y).Order().ToArray();
            var second = b.Select(p=>p.Position.Y).Order().ToArray();
            Assert.Equal(first.Length, second.Length);
            for (var i = 0; i < first.Length; i++) Assert.Equal(first[i], second[i], 5);
        }
    }
}
