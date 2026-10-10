using System.Numerics;

namespace ProjectG.NavigationStudy;

public readonly record struct StudyLink(int To, Vector3 Portal);
public sealed class StudyGraph(Vector3[] positions, List<StudyLink>[] links)
{
    public Vector3[] Positions { get; } = positions;
    public List<StudyLink>[] Links { get; } = links;
    public int LinkCount => Links.Sum(x => x.Count);
}

public enum SearchStatus { Success, Unreachable, BudgetExceeded, StaleRevision, InvalidPoint }

// One workspace per simulation worker; after warmup queries reuse arrays/heap/output lists.
public sealed class StudySearch(StudyGraph graph)
{
    private readonly float[] _cost = new float[graph.Positions.Length];
    private readonly int[] _parent = new int[graph.Positions.Length];
    private readonly int[] _seen = new int[graph.Positions.Length];
    private readonly int[] _closed = new int[graph.Positions.Length];
    private readonly PriorityQueue<int, (float Cost, int Id)> _open = new();
    private int _generation;
    public int Expanded { get; private set; }

    public SearchStatus Find(int start, int goal, int budget, List<int> route, int[]? clusters = null, HashSet<int>? allowed = null,
        bool[]? blocked = null, Func<int, StudyLink, bool>? edgeAllowed = null)
    {
        route.Clear(); Expanded = 0;
        if ((uint)start >= graph.Positions.Length || (uint)goal >= graph.Positions.Length || budget <= 0)
            return SearchStatus.InvalidPoint;
        if (blocked is not null && (blocked[start] || blocked[goal])) return SearchStatus.InvalidPoint;
        if (_generation == int.MaxValue)
        {
            Array.Clear(_seen); Array.Clear(_closed); _generation = 0;
        }
        _generation++; _open.Clear();
        _cost[start] = 0; _parent[start] = -1; _seen[start] = _generation;
        _open.Enqueue(start, (Heuristic(start), start));
        while (_open.TryDequeue(out var current, out _))
        {
            if (_closed[current] == _generation) continue;
            if (++Expanded > budget) return SearchStatus.BudgetExceeded;
            _closed[current] = _generation;
            if (current == goal)
            {
                for (var id = goal; id >= 0; id = _parent[id]) route.Add(id);
                route.Reverse(); return SearchStatus.Success;
            }
            foreach (var link in graph.Links[current])
            {
                var next = link.To;
                if (_closed[next] == _generation || (clusters is not null && allowed is not null && !allowed.Contains(clusters[next]))) continue;
                if ((blocked is not null && blocked[next]) || (edgeAllowed is not null && !edgeAllowed(current, link))) continue;
                var cost = _cost[current] + Vector3.Distance(graph.Positions[current], link.Portal) + Vector3.Distance(link.Portal, graph.Positions[next]);
                if (_seen[next] == _generation && cost >= _cost[next]) continue;
                _seen[next] = _generation; _parent[next] = current; _cost[next] = cost;
                _open.Enqueue(next, (cost + Heuristic(next), next));
            }
        }
        return SearchStatus.Unreachable;

        float Heuristic(int node) => Vector3.Distance(graph.Positions[node], graph.Positions[goal]);
    }
}

public sealed class PolygonStudy
{
    private readonly StudyMesh _mesh;
    private readonly StudySearch _search;
    private readonly List<int> _route = [];
    public StudyGraph Graph { get; }
    public int Expanded => _search.Expanded;

    public PolygonStudy(StudyMesh mesh)
    {
        _mesh = mesh;
        Graph = new(mesh.Triangles.Select(t => t.Center).ToArray(), mesh.Edges.Select(edges => edges.Select(e => new StudyLink(e.To, e.Portal)).ToList()).ToArray());
        _search = new(Graph);
    }

    public SearchStatus Find(Vector3 start, Vector3 goal, string revision, List<Vector3> output, int budget = 20_000)
    {
        output.Clear();
        if (revision != _mesh.Revision) return SearchStatus.StaleRevision;
        if (!_mesh.TryLocate(start, .35f, out var from) || !_mesh.TryLocate(goal, .35f, out var to)) return SearchStatus.InvalidPoint;
        var status = _search.Find(from.Triangle, to.Triangle, budget, _route, blocked: _mesh.Blocked);
        if (status != SearchStatus.Success) return status;
        output.Add(from.Position);
        for (var i = 1; i < _route.Count; i++)
        {
            foreach (var link in Graph.Links[_route[i - 1]])
                if (link.To == _route[i]) { output.Add(link.Portal); break; }
        }
        output.Add(to.Position);
        // Portal midpoints retain every slope boundary. Production funnel/smoothing is intentionally deferred.
        return status;
    }
}
