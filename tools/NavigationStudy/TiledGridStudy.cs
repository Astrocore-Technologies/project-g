using System.Numerics;

namespace ProjectG.NavigationStudy;

public sealed class TiledGridStudy
{
    private readonly StudyMesh _mesh;
    private readonly float _cellSize;
    private readonly Dictionary<(int X, int Z), List<int>> _columns = [];
    private readonly SurfacePoint[] _surfaces;
    private readonly int[] _clusters;
    private readonly StudySearch _fineSearch;
    private readonly StudySearch _coarseSearch;
    private readonly List<int> _fineRoute = [];
    private readonly List<int> _coarseRoute = [];
    private readonly HashSet<int> _allowed = [];
    private readonly Func<int, StudyLink, bool> _edgeAllowed;
    public StudyGraph Graph { get; }
    public StudyGraph TileGraph { get; }
    public int Expanded { get; private set; }

    public TiledGridStudy(StudyMesh mesh, float cellSize = .5f, int tileCells = 16)
    {
        if (!float.IsFinite(cellSize) || cellSize < .25f || cellSize > 2 || tileCells is < 4 or > 64)
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        _mesh = mesh; _cellSize = cellSize;
        var samples = new List<SurfacePoint>();
        var surfaces = new List<SurfacePoint>();
        var keys = new List<(int X, int Z)>();
        var minX = mesh.Map.Vertices.Min(p => p.X); var maxX = mesh.Map.Vertices.Max(p => p.X);
        var minZ = mesh.Map.Vertices.Min(p => p.Z); var maxZ = mesh.Map.Vertices.Max(p => p.Z);
        var left = Cell(minX); var right = Cell(maxX); var top = Cell(minZ); var bottom = Cell(maxZ);
        if ((long)(right - left + 1) * (bottom - top + 1) > 2_000_000)
            throw new InvalidDataException("Raster area exceeds study budget.");
        for (var z = top; z <= bottom; z++)
        for (var x = left; x <= right; x++)
        {
            mesh.SampleColumn((x + .5f) * cellSize, (z + .5f) * cellSize, samples);
            foreach (var sample in samples)
            {
                if (surfaces.Count >= 250_000) throw new InvalidDataException("Raster nodes exceed study budget.");
                if (!_columns.TryGetValue((x, z), out var column)) _columns[(x, z)] = column = [];
                column.Add(surfaces.Count); surfaces.Add(sample); keys.Add((x, z));
            }
        }
        _surfaces = surfaces.ToArray();
        _edgeAllowed = (from, edge) => _mesh.Trace(_surfaces[from], _surfaces[edge.To]);
        var links = surfaces.Select(_ => new List<StudyLink>()).ToArray();
        for (var i = 0; i < surfaces.Count; i++)
        {
            var (x, z) = keys[i];
            LinkColumn(x + 1, z); LinkColumn(x, z + 1);
            void LinkColumn(int nx, int nz)
            {
                if (!_columns.TryGetValue((nx, nz), out var column)) return;
                foreach (var j in column)
                {
                    // A link is bidirectional only when both exact corridor traces succeed.
                    if (!mesh.Trace(surfaces[i], surfaces[j]) || !mesh.Trace(surfaces[j], surfaces[i])) continue;
                    var portal = (surfaces[i].Position + surfaces[j].Position) / 2;
                    links[i].Add(new(j, portal)); links[j].Add(new(i, portal));
                }
            }
        }
        Graph = new(surfaces.Select(p => p.Position).ToArray(), links);
        _fineSearch = new(Graph);
        _clusters = Enumerable.Repeat(-1, surfaces.Count).ToArray();
        var centers = new List<Vector3>();
        var queue = new Queue<int>();
        for (var node = 0; node < surfaces.Count; node++)
        {
            if (_clusters[node] >= 0) continue;
            var tile = Tile(keys[node]); var id = centers.Count; var sum = Vector3.Zero; var count = 0;
            queue.Enqueue(node); _clusters[node] = id;
            // A tile can contain disconnected physical floors. Each local component gets its own coarse node.
            while (queue.TryDequeue(out var current))
            {
                sum += surfaces[current].Position; count++;
                foreach (var edge in links[current])
                    if (_clusters[edge.To] < 0 && Tile(keys[edge.To]) == tile)
                    { _clusters[edge.To] = id; queue.Enqueue(edge.To); }
            }
            centers.Add(sum / count);
        }
        var coarseLinks = centers.Select(_ => new List<StudyLink>()).ToArray();
        var pairs = new HashSet<(int From, int To)>();
        for (var i = 0; i < links.Length; i++)
        foreach (var edge in links[i])
        {
            var a = _clusters[i]; var b = _clusters[edge.To];
            if (a == b || !pairs.Add((a, b))) continue;
            coarseLinks[a].Add(new(b, edge.Portal));
        }
        TileGraph = new(centers.ToArray(), coarseLinks);
        _coarseSearch = new(TileGraph);

        (int X, int Z) Tile((int X, int Z) p) => ((int)MathF.Floor((float)p.X / tileCells), (int)MathF.Floor((float)p.Z / tileCells));
    }

    public SearchStatus Find(Vector3 start, Vector3 goal, string revision, List<Vector3> output, int budget = 20_000)
    {
        output.Clear(); Expanded = 0;
        if (revision != _mesh.Revision) return SearchStatus.StaleRevision;
        if (!_mesh.TryLocate(start, .35f, out var from) || !_mesh.TryLocate(goal, .35f, out var to)) return SearchStatus.InvalidPoint;
        var a = Attach(from); var b = Attach(to);
        if (a < 0 || b < 0) return SearchStatus.InvalidPoint;
        var status = _coarseSearch.Find(_clusters[a], _clusters[b], budget, _coarseRoute);
        Expanded = _coarseSearch.Expanded;
        if (status != SearchStatus.Success) return status;
        _allowed.Clear();
        foreach (var cluster in _coarseRoute) _allowed.Add(cluster);
        if (Expanded >= budget) return SearchStatus.BudgetExceeded;
        var validator = _mesh.PatchGeneration == 0 ? null : _edgeAllowed;
        status = _fineSearch.Find(a, b, budget - Expanded, _fineRoute, _clusters, _allowed, edgeAllowed: validator);
        Expanded += _fineSearch.Expanded;
        // A patch may invalidate a coarse corridor. Fall back within the same total query budget.
        if (status == SearchStatus.Unreachable && validator is not null && Expanded < budget)
        {
            status = _fineSearch.Find(a, b, budget - Expanded, _fineRoute, edgeAllowed: validator);
            Expanded += _fineSearch.Expanded;
        }
        if (status != SearchStatus.Success) return status;
        output.Add(from.Position);
        AppendTrace(from, _surfaces[a]);
        output.Add(_surfaces[a].Position);
        for (var i = 1; i < _fineRoute.Count; i++)
        {
            var previous = _surfaces[_fineRoute[i - 1]]; var next = _surfaces[_fineRoute[i]];
            AppendTrace(previous, next); output.Add(next.Position);
        }
        AppendTrace(_surfaces[b], to);
        output.Add(to.Position);
        return status;

        void AppendTrace(SurfacePoint first, SurfacePoint second)
        {
            if (!_mesh.Trace(first, second, crossings: output))
                throw new InvalidOperationException("Validated raster link lost its surface corridor.");
        }
    }

    private int Attach(SurfacePoint point)
    {
        var x = Cell(point.Position.X); var z = Cell(point.Position.Z); var closest = -1; var distance = float.PositiveInfinity;
        for (var dz = -1; dz <= 1; dz++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (!_columns.TryGetValue((x + dx, z + dz), out var column)) continue;
            foreach (var id in column)
            {
                var d = Vector3.DistanceSquared(point.Position, _surfaces[id].Position);
                if (d >= distance || !_mesh.Trace(point, _surfaces[id])) continue;
                distance = d; closest = id;
            }
        }
        return closest;
    }

    private int Cell(float value) => (int)MathF.Floor(value / _cellSize);
}
