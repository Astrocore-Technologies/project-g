using System.Numerics;

namespace Content.Shared.Navigation;

// Separate bounded broad-phase for attack visibility: walkability alone cannot represent a ceiling.
public sealed class SurfaceVisibility
{
    private const float TileSize = 4;
    private readonly Dictionary<(int X, int Z), List<int>> _tiles = [];
    private readonly (Vector3 Center, Vector3 HalfSize, Quaternion Inverse)[] _boxes;
    private readonly (Vector3 A, Vector3 B, Vector3 C)[] _ground;
    private readonly int[] _seen;
    private int _generation;

    public SurfaceVisibility(SurfaceGeometry map)
    {
        map.Validate();
        _boxes = new (Vector3, Vector3, Quaternion)[map.Occluders.Length];
        // A baked outdoor surface must also stop rays through hills. Reuse the public
        // polygons with a bounded spatial index; never scan the entire region in combat.
        var ground = new List<(Vector3, Vector3, Vector3)>();
        foreach (var polygon in map.WalkableFacesOcclude ? map.Polygons : [])
            for (var j = 1; j + 1 < polygon.Length; j++)
                ground.Add((map.Vertices[polygon[0]].Vector, map.Vertices[polygon[j]].Vector, map.Vertices[polygon[j + 1]].Vector));
        foreach (var face in map.OcclusionFaces ?? [])
            ground.Add((map.Vertices[face[0]].Vector,map.Vertices[face[1]].Vector,map.Vertices[face[2]].Vector));
        _ground = ground.ToArray();
        _seen = new int[_boxes.Length + _ground.Length];
        long references = 0;
        for (var i = 0; i < _boxes.Length; i++)
        {
            var box = map.Occluders[i]; var r = box.Rotation;
            var q = Quaternion.CreateFromYawPitchRoll(r.Y, r.X, r.Z);
            _boxes[i] = (box.Center.Vector, box.Size.Vector / 2, Quaternion.Inverse(q));
            var extent = Vector3.Zero;
            for (var corner = 0; corner < 8; corner++)
            {
                var half = box.Size.Vector / 2;
                var v = Vector3.Transform(new Vector3((corner & 1) == 0 ? half.X : -half.X,
                    (corner & 2) == 0 ? half.Y : -half.Y, (corner & 4) == 0 ? half.Z : -half.Z), q);
                extent = Vector3.Max(extent, Vector3.Abs(v));
            }
            for (var z = Tile(box.Center.Z - extent.Z); z <= Tile(box.Center.Z + extent.Z); z++)
            for (var x = Tile(box.Center.X - extent.X); x <= Tile(box.Center.X + extent.X); x++)
            {
                if (++references > 1_000_000) throw new InvalidDataException("Visibility index exceeds budget.");
                if (!_tiles.TryGetValue((x, z), out var list)) _tiles[(x, z)] = list = [];
                list.Add(i);
            }
        }
        for (var i = 0; i < _ground.Length; i++)
        {
            var triangle = _ground[i];
            var min = Vector3.Min(triangle.A, Vector3.Min(triangle.B, triangle.C));
            var max = Vector3.Max(triangle.A, Vector3.Max(triangle.B, triangle.C));
            for (var z = Tile(min.Z); z <= Tile(max.Z); z++)
            for (var x = Tile(min.X); x <= Tile(max.X); x++)
            {
                if (++references > 1_000_000) throw new InvalidDataException("Visibility index exceeds budget.");
                if (!_tiles.TryGetValue((x, z), out var list)) _tiles[(x, z)] = list = [];
                list.Add(_boxes.Length + i);
            }
        }
    }

    public bool Clear(Vector3 start, Vector3 end)
    {
        if (!Finite(start) || !Finite(end) || Vector3.DistanceSquared(start, end) > 128 * 128) return false;
        if (_generation == int.MaxValue) { Array.Clear(_seen); _generation = 0; }
        _generation++;
        var min = Vector3.Min(start, end); var max = Vector3.Max(start, end);
        for (var z = Tile(min.Z); z <= Tile(max.Z); z++)
        for (var x = Tile(min.X); x <= Tile(max.X); x++)
        {
            if (!_tiles.TryGetValue((x, z), out var list)) continue;
            foreach (var id in list)
            {
                if (_seen[id] == _generation) continue;
                _seen[id] = _generation;
                if (id >= _boxes.Length)
                {
                    var triangle = _ground[id - _boxes.Length];
                    if (IntersectsTriangle(start, end - start, triangle.A, triangle.B, triangle.C)) return false;
                    continue;
                }
                var box = _boxes[id];
                var a = Vector3.Transform(start - box.Center, box.Inverse);
                var b = Vector3.Transform(end - box.Center, box.Inverse);
                if (Intersects(a, b - a, box.HalfSize)) return false;
            }
        }
        return true;
    }

    private static bool IntersectsTriangle(Vector3 start, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        // Two-sided segment/triangle test, including the underside of bridges.
        // Endpoint epsilon avoids treating a ray starting on a surface as an obstruction.
        var edge1 = b - a; var edge2 = c - a;
        var cross = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, cross);
        if (MathF.Abs(determinant) < .000001f) return false;
        var inverse = 1 / determinant;
        var offset = start - a;
        var u = Vector3.Dot(offset, cross) * inverse;
        if (u < -.00001f || u > 1.00001f) return false;
        var q = Vector3.Cross(offset, edge1);
        var v = Vector3.Dot(direction, q) * inverse;
        if (v < -.00001f || u + v > 1.00001f) return false;
        var t = Vector3.Dot(edge2, q) * inverse;
        return t > .0001f && t < .9999f;
    }

    private static bool Intersects(Vector3 start, Vector3 direction, Vector3 half)
    {
        var enter = 0f; var exit = 1f;
        for (var axis = 0; axis < 3; axis++)
        {
            var s = axis == 0 ? start.X : axis == 1 ? start.Y : start.Z;
            var d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            var h = axis == 0 ? half.X : axis == 1 ? half.Y : half.Z;
            if (MathF.Abs(d) < .000001f) { if (MathF.Abs(s) > h) return false; continue; }
            var a = (-h - s) / d; var b = (h - s) / d;
            if (a > b) (a, b) = (b, a);
            enter = MathF.Max(enter, a); exit = MathF.Min(exit, b);
            if (enter > exit) return false;
        }
        return exit >= 0 && enter <= 1;
    }

    private static int Tile(float value) => (int)MathF.Floor(value / TileSize);
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
