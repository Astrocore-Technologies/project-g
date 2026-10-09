using System.Numerics;

namespace Content.Shared.Navigation;

/// <summary>Bounded deterministic A*. Scratch buffers are reused; not thread-safe.</summary>
public sealed class NavigationPathfinder
{
    private readonly NavigationGrid _grid;
    private readonly bool[] _walkable;
    private readonly int[] _cost;
    private readonly int[] _parents;
    private readonly bool[] _closed;
    private readonly PriorityQueue<int, (int Score, int Id)> _open = new();
    private readonly List<Vector2> _rawPath = new();
    private uint _revision;

    public NavigationPathfinder(NavigationGrid grid)
    {
        _grid = grid;
        _walkable = new bool[grid.CellCount];
        _cost = new int[grid.CellCount];
        _parents = new int[grid.CellCount];
        _closed = new bool[grid.CellCount];
        for (var i = 0; i < grid.CellCount; i++)
            _walkable[i] = grid.IsWalkable(grid.Center(i));
    }

    public bool TryFindPath(Vector2 from, Vector2 target, List<Vector2> result)
    {
        if (_revision != _grid.Revision)
        {
            for (var i = 0; i < _walkable.Length; i++) _walkable[i] = _grid.IsWalkable(_grid.Center(i));
            _revision = _grid.Revision;
        }
        result.Clear();
        if (!_grid.IsWalkable(from) || !_grid.IsWalkable(target))
            return false;
        if (_grid.CanTraverse(from, target))
        {
            result.Add(target);
            return true;
        }
        var start = _grid.Cell(from);
        var goal = _grid.Cell(target);
        if (!_walkable[start] || !_walkable[goal])
            return false;
        Array.Fill(_cost, int.MaxValue);
        Array.Fill(_parents, -1);
        Array.Clear(_closed);
        _open.Clear();
        _cost[start] = 0;
        _open.Enqueue(start, (Heuristic(start, goal), start));
        while (_open.TryDequeue(out var current, out _))
        {
            if (_closed[current])
                continue;
            if (current == goal)
                return BuildPath(goal, from, target, result);
            _closed[current] = true;
            var x = current % _grid.Width;
            var z = current / _grid.Width;
            if (x > 0) Visit(current, current - 1, goal);
            if (x + 1 < _grid.Width) Visit(current, current + 1, goal);
            if (z > 0) Visit(current, current - _grid.Width, goal);
            if (z + 1 < _grid.Height) Visit(current, current + _grid.Width, goal);
        }
        return false;
    }

    private void Visit(int from, int to, int goal)
    {
        if (_closed[to] || !_walkable[to] || _cost[from] + 1 >= _cost[to] ||
            !_grid.CanTraverse(_grid.Center(from), _grid.Center(to)))
            return;
        _cost[to] = _cost[from] + 1;
        _parents[to] = from;
        _open.Enqueue(to, (_cost[to] + Heuristic(to, goal), to));
    }

    private int Heuristic(int from, int to) =>
        Math.Abs(from % _grid.Width - to % _grid.Width) +
        Math.Abs(from / _grid.Width - to / _grid.Width);

    private bool BuildPath(int goal, Vector2 from, Vector2 target, List<Vector2> result)
    {
        _rawPath.Clear();
        for (var cell = goal; cell != -1; cell = _parents[cell])
            _rawPath.Add(_grid.Center(cell));
        _rawPath.Reverse();
        _rawPath.Add(target);
        var position = from;
        var next = 0;
        // Greedy line-of-sight smoothing removes stair steps without cutting blocked corners.
        while (next < _rawPath.Count)
        {
            var reachable = next;
            if (!_grid.CanTraverse(position, _rawPath[reachable]))
            {
                result.Clear();
                return false;
            }
            while (reachable + 1 < _rawPath.Count && _grid.CanTraverse(position, _rawPath[reachable + 1]))
                reachable++;
            position = _rawPath[reachable];
            result.Add(position);
            next = reachable + 1;
        }
        return true;
    }
}
