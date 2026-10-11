namespace ProjectG.NavigationStudy;

/// <summary>Offline packing only; retains the original triangle diagonal and heights.</summary>
public static class SurfaceExportPacking
{
    public static (int[] SourceVertices,int[][] Polygons) CompactVertices(IReadOnlyList<int[]> polygons)
    {
        var used = polygons.SelectMany(p=>p).Distinct().Order().ToArray();
        var remap = used.Select((source,index)=>(source,index)).ToDictionary(p=>p.source,p=>p.index);
        return (used,polygons.Select(p=>p.Select(index=>remap[index]).ToArray()).ToArray());
    }

    // Recast detail meshes can contain vertical/zero-area slivers. They have
    // no walkable X/Z footprint; retain every remaining original fan face.
    public static int[][] RemoveDegenerateFaces(IReadOnlyList<(float X, float Z)> vertices, IReadOnlyList<int[]> polygons)
    {
        var output = new List<int[]>(polygons.Count);
        foreach (var p in polygons)
        {
            bool Valid(int i)
            {
                var a = vertices[p[0]]; var b = vertices[p[i]]; var c = vertices[p[i + 1]];
                return MathF.Abs((b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X)) >= .00001f;
            }
            if (Enumerable.Range(1, p.Length-2).All(Valid)) output.Add(p);
            else for (var i = 1; i+1 < p.Length; i++)
                if (Valid(i)) output.Add([p[0],p[i],p[i+1]]);
        }
        return output.ToArray();
    }

    public static int[][] PairTriangles(IReadOnlyList<int[]> polygons)
    {
        var edges = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < polygons.Count; i++)
        {
            var p = polygons[i];
            if (p.Length != 3) continue;
            for (var j = 0; j < 3; j++)
            {
                var a = p[j]; var b = p[(j+1)%3];
                var key = a < b ? (a,b) : (b,a);
                if (!edges.TryGetValue(key, out var owners)) edges[key] = owners = [];
                owners.Add(i);
            }
        }
        var used = new bool[polygons.Count];
        var output = new List<int[]>(polygons.Count);
        for (var i = 0; i < polygons.Count; i++)
        {
            if (used[i]) continue;
            var p = polygons[i];
            int[]? packed = null;
            if (p.Length == 3)
                for (var j = 0; j < 3 && packed is null; j++)
                {
                    var a = p[j]; var b = p[(j+1)%3]; var c = p[(j+2)%3];
                    var owners = edges[a < b ? (a,b) : (b,a)];
                    if (owners.Count != 2) continue;
                    var other = owners[0] == i ? owners[1] : owners[0];
                    if (used[other]) continue;
                    var d = polygons[other].Single(v => v != a && v != b);
                    if (d == c) continue;
                    // Fan triangles are (a,c,b) and (a,b,d): exactly the original
                    // two faces, even on a curved, non-planar terrain quad.
                    packed = [a,c,b,d]; used[other] = true;
                }
            used[i] = true;
            output.Add(packed ?? p);
        }
        return output.ToArray();
    }
}
