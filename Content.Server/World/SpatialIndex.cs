using System.Numerics;
using Content.Shared.Network;

namespace Content.Server.World;

/// <summary>Single-threaded uniform grid. Queries visit nearby buckets, not every entity.</summary>
public sealed class SpatialIndex
{
    private readonly float _cellSize;
    private readonly Dictionary<(int X, int Z), HashSet<NetworkEntityId>> _cells = new();
    private readonly Dictionary<NetworkEntityId, Vector2> _positions = new();

    public SpatialIndex(float cellSize)
    {
        if (!float.IsFinite(cellSize) || cellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        _cellSize = cellSize;
    }

    public int Count => _positions.Count;
    public int CellCount => _cells.Count;

    public void Add(NetworkEntityId id, Vector2 position)
    {
        Validate(position);
        if (!id.IsValid)
            throw new ArgumentOutOfRangeException(nameof(id));
        var cell = Cell(position);
        _positions.Add(id, position);
        AddToCell(id, cell);
    }

    public void Move(NetworkEntityId id, Vector2 position)
    {
        Validate(position);
        var previous = _positions[id];
        var previousCell = Cell(previous);
        var nextCell = Cell(position);
        if (previousCell != nextCell)
        {
            RemoveFromCell(id, previousCell);
            AddToCell(id, nextCell);
        }
        _positions[id] = position;
    }

    public bool Remove(NetworkEntityId id)
    {
        if (!_positions.Remove(id, out var previous))
            return false;
        RemoveFromCell(id, Cell(previous));
        return true;
    }

    public void Query(Vector2 center, float radius, HashSet<NetworkEntityId> results)
    {
        Validate(center);
        if (!float.IsFinite(radius) || radius < 0f || radius / _cellSize > 32f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        results.Clear();
        var min = Cell(center - new Vector2(radius));
        var max = Cell(center + new Vector2(radius));
        var squaredRadius = radius * radius;
        for (long x = min.X; x <= max.X; x++)
        {
            for (long z = min.Z; z <= max.Z; z++)
            {
                if (!_cells.TryGetValue(((int) x, (int) z), out var ids))
                    continue;
                foreach (var id in ids)
                {
                    if (Vector2.DistanceSquared(center, _positions[id]) <= squaredRadius)
                        results.Add(id);
                }
            }
        }
    }

    private (int X, int Z) Cell(Vector2 position) =>
        (checked((int) MathF.Floor(position.X / _cellSize)),
         checked((int) MathF.Floor(position.Y / _cellSize)));

    private static void Validate(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
    }

    private void AddToCell(NetworkEntityId id, (int X, int Z) cell)
    {
        if (!_cells.TryGetValue(cell, out var ids))
        {
            ids = new HashSet<NetworkEntityId>();
            _cells.Add(cell, ids);
        }
        ids.Add(id);
    }

    private void RemoveFromCell(NetworkEntityId id, (int X, int Z) cell)
    {
        var ids = _cells[cell];
        ids.Remove(id);
        // Empty buckets must not accumulate as entities travel through the world.
        if (ids.Count == 0)
            _cells.Remove(cell);
    }
}
