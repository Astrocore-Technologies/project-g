using System.Numerics;

namespace Content.Shared.Navigation;

public readonly record struct SurfaceLocation(int Triangle, Vector3 Position);
public readonly record struct SurfaceTriangle(Vector3 A, Vector3 B, Vector3 C)
{
    public Vector3 Center => (A + B + C) / 3;

    public bool Sample(float x, float z, out float y, float tolerance = .0001f)
    {
        var denominator = (B.Z - C.Z) * (A.X - C.X) + (C.X - B.X) * (A.Z - C.Z);
        var a = ((B.Z - C.Z) * (x - C.X) + (C.X - B.X) * (z - C.Z)) / denominator;
        var b = ((C.Z - A.Z) * (x - C.X) + (A.X - C.X) * (z - C.Z)) / denominator;
        y = a * A.Y + b * B.Y + (1 - a - b) * C.Y;
        return a >= -tolerance && b >= -tolerance && a + b <= 1 + tolerance;
    }
}

public sealed class SurfaceNavigation
{
    private const float BucketSize = 4;
    private readonly Dictionary<(int X, int Z), List<int>> _buckets = [];
    public SurfaceGeometry Map { get; }
    public SurfaceTriangle[] Triangles { get; }
    public List<SurfaceEdge>[] Edges { get; }
    public bool[] Blocked { get; }
    public string Revision { get; private set; }
    public int PatchGeneration { get; private set; }

    public SurfaceNavigation(SurfaceGeometry map)
    {
        map.Validate();
        Map = map;
        Revision = map.Revision;
        var triangles = new List<SurfaceTriangle>();
        foreach (var p in map.Polygons)
        for (var i = 1; i + 1 < p.Length; i++)
        {
            var t = new SurfaceTriangle(map.Vertices[p[0]].Vector, map.Vertices[p[i]].Vector, map.Vertices[p[i + 1]].Vector);
            var projectedArea = (t.B.X - t.A.X) * (t.C.Z - t.A.Z) - (t.B.Z - t.A.Z) * (t.C.X - t.A.X);
            if (MathF.Abs(projectedArea) < .00001f) throw new InvalidDataException("Degenerate walkable triangle.");
            triangles.Add(t);
        }
        Triangles = triangles.ToArray();
        Blocked = new bool[Triangles.Length];
        Edges = new List<SurfaceEdge>[Triangles.Length];
        var owners = new Dictionary<(VertexKey, VertexKey), (int Triangle, Vector3 A, Vector3 B)>();
        var usedEdges = new HashSet<(VertexKey, VertexKey)>();
        long bucketReferences = 0;
        for (var i = 0; i < Triangles.Length; i++)
        {
            Edges[i] ??= [];
            var t = Triangles[i];
            AddEdge(t.A, t.B); AddEdge(t.B, t.C); AddEdge(t.C, t.A);
            var min = Vector3.Min(Vector3.Min(t.A, t.B), t.C);
            var max = Vector3.Max(Vector3.Max(t.A, t.B), t.C);
            for (var z = Bucket(min.Z); z <= Bucket(max.Z); z++)
            for (var x = Bucket(min.X); x <= Bucket(max.X); x++)
            {
                if (++bucketReferences > 2_000_000) throw new InvalidDataException("Surface spatial index exceeds budget.");
                if (!_buckets.TryGetValue((x, z), out var list)) _buckets[(x, z)] = list = [];
                list.Add(i);
            }

            void AddEdge(Vector3 a, Vector3 b)
            {
                // Full XYZ keys are essential: a bridge and its underpass cannot share edges by X/Z alone.
                var ka = VertexKey.From(a); var kb = VertexKey.From(b);
                var key = ka.CompareTo(kb) < 0 ? (ka, kb) : (kb, ka);
                if (usedEdges.Contains(key)) throw new InvalidDataException("Non-manifold navigation edge.");
                if (owners.Remove(key, out var owner))
                {
                    Edges[owner.Triangle].Add(new(i, a, b));
                    Edges[i].Add(new(owner.Triangle, a, b));
                    usedEdges.Add(key);
                }
                else owners.Add(key, (i, a, b));
            }
        }
    }

    public bool TryLocate(Vector3 requested, float heightTolerance, out SurfaceLocation point)
    {
        point = default;
        if (!Finite(requested) || !float.IsFinite(heightTolerance) || heightTolerance < 0 || heightTolerance > 1)
            return false;
        if (!_buckets.TryGetValue((Bucket(requested.X), Bucket(requested.Z)), out var candidates)) return false;
        var best = float.PositiveInfinity;
        foreach (var id in candidates)
        {
            if (Blocked[id]) continue;
            if (!Triangles[id].Sample(requested.X, requested.Z, out var y)) continue;
            var difference = MathF.Abs(y - requested.Y);
            if (difference > heightTolerance || difference >= best) continue;
            best = difference;
            point = new(id, new(requested.X, y, requested.Z));
        }
        return best != float.PositiveInfinity;
    }

    public void SampleColumn(float x, float z, List<SurfaceLocation> output)
    {
        output.Clear();
        if (!_buckets.TryGetValue((Bucket(x), Bucket(z)), out var candidates)) return;
        foreach (var id in candidates)
        {
            if (Blocked[id]) continue;
            if (!Triangles[id].Sample(x, z, out var y)) continue;
            // Adjacent triangles may contain the same point on an edge, but floors stay distinct.
            if (output.Any(p => MathF.Abs(p.Position.Y - y) < .001f)) continue;
            output.Add(new(id, new(x, y, z)));
        }
    }

    public bool Trace(SurfaceLocation start, SurfaceLocation goal, int maxCrossings = 4096, List<Vector3>? crossings = null)
    {
        if ((uint)start.Triangle >= Triangles.Length || (uint)goal.Triangle >= Triangles.Length ||
            !Finite(start.Position) || !Finite(goal.Position) || maxCrossings <= 0 ||
            Blocked[start.Triangle] || Blocked[goal.Triangle]) return false;
        var origin = new Vector2(start.Position.X, start.Position.Z);
        var direction = new Vector2(goal.Position.X, goal.Position.Z) - origin;
        if (direction.LengthSquared() < .000001f)
            return Vector3.DistanceSquared(start.Position, goal.Position) < .0001f;
        var current = start.Triangle;
        var parameter = -.0001f;
        for (var crossed = 0; crossed < maxCrossings; crossed++)
        {
            // A portal endpoint belongs to both triangles. Do not require crossing beyond t=1,
            // where floating-point barycentric tests can select the other owner of the same edge.
            if (current == goal.Triangle || Triangles[current].Sample(goal.Position.X, goal.Position.Z, out var endHeight) &&
                MathF.Abs(endHeight-goal.Position.Y) < .001f) return true;
            var next = -1;
            var exit = float.PositiveInfinity;
            foreach (var edge in Edges[current])
            {
                if (Blocked[edge.To]) continue;
                var a = new Vector2(edge.A.X, edge.A.Z);
                var span = new Vector2(edge.B.X, edge.B.Z) - a;
                var denominator = Cross(direction, span);
                if (MathF.Abs(denominator) < .000001f) continue;
                var t = Cross(a - origin, span) / denominator;
                var u = Cross(a - origin, direction) / denominator;
                if (t <= parameter + .00001f || t > 1.0001f || u < -.0001f || u > 1.0001f || t >= exit) continue;
                // A vertex hit can have two incident edges. Select the triangle beyond the crossing.
                var probe = origin + direction * MathF.Min(t + .0001f, 1);
                if (!Triangles[edge.To].Sample(probe.X, probe.Y, out _, .0000001f)) continue;
                next = edge.To; exit = t;
            }
            if (next < 0) return false;
            var crossedPoint = origin + direction * exit;
            Triangles[current].Sample(crossedPoint.X, crossedPoint.Y, out var height);
            crossings?.Add(new(crossedPoint.X, height, crossedPoint.Y));
            current = next; parameter = exit;
        }
        return false;
    }

    public void ApplyPatch(IReadOnlyList<int> triangles, bool blocked)
    {
        if (triangles.Count > 4096 || PatchGeneration == int.MaxValue)
            throw new InvalidDataException("Patch exceeds surface budget.");
        // Validate the entire patch before mutating any surface.
        foreach (var id in triangles)
            if ((uint)id >= Triangles.Length) throw new InvalidDataException("Invalid patch surface.");
        foreach (var id in triangles) Blocked[id] = blocked;
        PatchGeneration++;
        Revision = $"{Map.Revision}:{PatchGeneration}";
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static int Bucket(float value) => (int)MathF.Floor(value / BucketSize);
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private readonly record struct VertexKey(int X, int Y, int Z) : IComparable<VertexKey>
    {
        // Quantization is used only for matching exported edges, not as a production coordinate contract.
        public static VertexKey From(Vector3 p) => new((int)MathF.Round(p.X * 1000), (int)MathF.Round(p.Y * 1000), (int)MathF.Round(p.Z * 1000));
        public int CompareTo(VertexKey other)
        {
            var c = X.CompareTo(other.X); if (c != 0) return c;
            c = Y.CompareTo(other.Y); return c != 0 ? c : Z.CompareTo(other.Z);
        }
    }
}

public readonly record struct SurfaceEdge(int To, Vector3 A, Vector3 B)
{
    public Vector3 Portal => (A + B) / 2;
}
