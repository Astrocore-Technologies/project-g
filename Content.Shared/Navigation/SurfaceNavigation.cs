using System.Numerics;

namespace Content.Shared.Navigation;

public readonly record struct SurfaceLocation(int Triangle, Vector3 Position);
public readonly record struct SurfaceTriangle(Vector3 A, Vector3 B, Vector3 C)
{
    public Vector3 Center => (A + B + C) / 3;

    public bool Sample(double x, double z, out float y, float tolerance = .0001f)
    {
        // Large world coordinates and thin baked triangles need double intermediates.
        // The tolerance is a distance in metres, rather than a barycentric fraction
        // that would admit centimetres outside the edge of a large polygon.
        var denominator = ((double)B.Z-C.Z)*((double)A.X-C.X)+((double)C.X-B.X)*((double)A.Z-C.Z);
        if (Math.Abs(denominator)<1e-12) { y=0; return false; }
        var a = (((double)B.Z-C.Z)*((double)x-C.X)+((double)C.X-B.X)*((double)z-C.Z))/denominator;
        var b = (((double)C.Z-A.Z)*((double)x-C.X)+((double)A.X-C.X)*((double)z-C.Z))/denominator;
        var c = 1-a-b;
        y = (float)(a*A.Y+b*B.Y+c*C.Y);
        if (a>=0 && b>=0 && c>=0) return true;
        if (tolerance<=0) return false;
        var scale = tolerance/Math.Abs(denominator);
        static double Length(Vector3 p,Vector3 q)
        { var dx=(double)p.X-q.X; var dz=(double)p.Z-q.Z; return Math.Sqrt(dx*dx+dz*dz); }
        return a >= -scale*Length(B,C) && b >= -scale*Length(C,A) && c >= -scale*Length(A,B);
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
                if (usedEdges.Contains(key)) throw new InvalidDataException($"Non-manifold navigation edge {a} -> {b} at triangle {i}.");
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
        // Prefer the actual containing face. A tolerant test may also accept
        // the previous face just beyond a portal, stranding the next movement tick.
        for (var pass=0; pass<2; pass++)
        {
            var best = float.PositiveInfinity;
            foreach (var id in candidates)
            {
                if (Blocked[id]) continue;
                if (!Triangles[id].Sample(requested.X, requested.Z, out var y,pass==0 ? 0 : .0001f)) continue;
                var difference = MathF.Abs(y - requested.Y);
                if (difference > heightTolerance || difference >= best) continue;
                best = difference;
                point = new(id, new(requested.X, y, requested.Z));
            }
            if (best != float.PositiveInfinity) return true;
        }
        return false;
    }

    public bool HasWalkableColumn(float x, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z) || !_buckets.TryGetValue((Bucket(x), Bucket(z)), out var candidates)) return false;
        foreach (var id in candidates)
            if (!Blocked[id] && Triangles[id].Sample(x, z, out _)) return true;
        return false;
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
        var dx = (double)goal.Position.X-origin.X;
        var dz = (double)goal.Position.Z-origin.Y;
        var length = Math.Sqrt(dx*dx+dz*dz);
        if (length < .001)
            return Vector3.DistanceSquared(start.Position, goal.Position) < .0001f;
        var current = start.Triangle;
        var parameter = -1e-8;
        for (var crossed = 0; crossed < maxCrossings; crossed++)
        {
            // A portal endpoint belongs to both triangles. Do not require crossing beyond t=1,
            // where floating-point barycentric tests can select the other owner of the same edge.
            if (current == goal.Triangle || Triangles[current].Sample(goal.Position.X, goal.Position.Z, out var endHeight) &&
                MathF.Abs(endHeight-goal.Position.Y) < .001f) return true;
            var next = -1;
            var exit = double.PositiveInfinity;
            foreach (var edge in Edges[current])
            {
                if (Blocked[edge.To]) continue;
                // Convert before subtraction too: opposite-sign coordinates can
                // lose precision even when the subsequent cross product is double.
                var ax = (double)edge.A.X-origin.X;
                var az = (double)edge.A.Z-origin.Y;
                var sx = (double)edge.B.X-edge.A.X;
                var sz = (double)edge.B.Z-edge.A.Z;
                var denominator = Cross(dx,dz,sx,sz);
                if (Math.Abs(denominator) < 1e-12) continue;
                var t = Cross(ax,az,sx,sz) / denominator;
                var u = Cross(ax,az,dx,dz) / denominator;
                if (t <= parameter + 1e-10 || t > 1.00000001 || u < -1e-8 || u > 1.00000001 || t >= exit) continue;
                // A vertex hit can have two incident edges. Select the triangle beyond the crossing.
                // Keep the probe in double precision. Casting it back to world
                // floats loses small progress, while a 1 mm nudge skips thin faces.
                var probe = Math.Min(t + 1e-7 / length,1);
                var target = edge.To;
                if (!Triangles[target].Sample(origin.X+dx*probe,origin.Y+dz*probe,out _,0))
                {
                    // A line through a shared vertex can enter a face that is
                    // two portals away. Follow only the connected fan around
                    // that exact vertex; never step across a gap or another floor.
                    var vertex = Math.Abs(u) < 1e-8 ? edge.A : Math.Abs(u-1) < 1e-8 ? edge.B : (Vector3?)null;
                    if (vertex is not { } pivot || !TryVertexFan(current, pivot,
                        origin.X+dx*probe, origin.Y+dz*probe, out target)) continue;
                }
                next = target; exit = t;
            }
            if (next < 0) return false;
            var crossedPoint = new Vector2((float)(origin.X+dx*exit),(float)(origin.Y+dz*exit));
            Triangles[current].Sample(crossedPoint.X, crossedPoint.Y, out var height);
            crossings?.Add(new(crossedPoint.X, height, crossedPoint.Y));
            current = next; parameter = exit;
        }
        return false;
    }

    private bool TryVertexFan(int start, Vector3 vertex, double x, double z, out int target)
    {
        // Ordinary manifold vertices have few incident faces. A bounded stack
        // keeps shared movement/prediction allocation-free and rejects pathological fans.
        Span<int> visited = stackalloc int[64];
        visited[0] = start;
        var count = 1;
        for (var cursor = 0; cursor < count; cursor++)
        {
            var triangle = visited[cursor];
            if (triangle != start && Triangles[triangle].Sample(x, z, out _, 0))
            { target = triangle; return true; }
            foreach (var edge in Edges[triangle])
            {
                if (Blocked[edge.To] || edge.A != vertex && edge.B != vertex || visited[..count].Contains(edge.To)) continue;
                if (count == visited.Length) { target = -1; return false; }
                visited[count++] = edge.To;
            }
        }
        target = -1;
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

    private static double Cross(double ax,double az,double bx,double bz) => ax*bz-az*bx;
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
