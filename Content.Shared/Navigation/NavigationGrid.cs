using System.Numerics;
using Content.Shared.Network;

namespace Content.Shared.Navigation;

/// <summary>Bounded flat geometry shared by authority and prediction; updates only open cells.</summary>
public sealed class NavigationGrid
{
    private readonly byte[] _blocked;
    public Vector2 Origin { get; }
    public float CellSize { get; }
    public float AgentRadius { get; }
    public int Width { get; }
    public int Height { get; }
    public int CellCount => Width * Height;
    public uint Revision { get; private set; }

    public NavigationGrid(RegionNavigation data)
    {
        if (!IsValid(data))
            throw new ArgumentException("Invalid navigation grid.", nameof(data));
        Origin = data.Origin;
        CellSize = data.CellSize;
        AgentRadius = data.AgentRadius;
        Width = data.Width;
        Height = data.Height;
        _blocked = (byte[]) data.BlockedCells.Clone();
    }

    public static bool IsValid(RegionNavigation data)
    {
        var count = (long) data.Width * data.Height;
        if (data.Width == 0 || data.Height == 0 || count > NetworkConstants.MaxNavigationCells ||
            !float.IsFinite(data.Origin.X) || !float.IsFinite(data.Origin.Y) ||
            !float.IsFinite(data.CellSize) || data.CellSize is < 0.1f or > 100f ||
            !float.IsFinite(data.AgentRadius) || data.AgentRadius <= 0f ||
            data.AgentRadius >= data.CellSize * 0.5f ||
            !float.IsFinite(data.Origin.X + data.Width * data.CellSize) ||
            !float.IsFinite(data.Origin.Y + data.Height * data.CellSize) ||
            data.BlockedCells is null || data.BlockedCells.Length != count)
            return false;
        foreach (var cell in data.BlockedCells)
        {
            if (cell > 1)
                return false;
        }
        return true;
    }

    public void ApplyOpening(RegionNavigation data)
    {
        if (!IsValid(data) || data.Origin!=Origin || data.CellSize!=CellSize || data.AgentRadius!=AgentRadius || data.Width!=Width || data.Height!=Height) throw new ArgumentException("Opening changed grid geometry.");
        for (var i=0;i<_blocked.Length;i++) if (_blocked[i]==0 && data.BlockedCells[i]!=0) throw new ArgumentException("Opening cannot close a cell.");
        data.BlockedCells.CopyTo(_blocked,0);
        Revision++;
    }
    public RegionNavigation ToMessage() => new(Origin, CellSize, AgentRadius,
        (ushort) Width, (ushort) Height, (byte[]) _blocked.Clone());

    public bool IsBlocked(int x, int z) =>
        x < 0 || z < 0 || x >= Width || z >= Height || _blocked[z * Width + x] != 0;

    public Vector2 Center(int index) => Origin +
        new Vector2(index % Width + 0.5f, index / Width + 0.5f) * CellSize;

    public int Cell(Vector2 position) =>
        (int) MathF.Floor((position.Y - Origin.Y) / CellSize) * Width +
        (int) MathF.Floor((position.X - Origin.X) / CellSize);

    public bool IsWalkable(Vector2 position) => CanTraverse(position, position);

    public bool CanTraverse(Vector2 from, Vector2 to)
    {
        if (!InsideBounds(from) || !InsideBounds(to))
            return false;
        var min = Vector2.Min(from, to) - new Vector2(AgentRadius);
        var max = Vector2.Max(from, to) + new Vector2(AgentRadius);
        var minX = Math.Max(0, (int) MathF.Floor((min.X - Origin.X) / CellSize));
        var minZ = Math.Max(0, (int) MathF.Floor((min.Y - Origin.Y) / CellSize));
        var maxX = Math.Min(Width - 1, (int) MathF.Floor((max.X - Origin.X) / CellSize));
        var maxZ = Math.Min(Height - 1, (int) MathF.Floor((max.Y - Origin.Y) / CellSize));
        for (var z = minZ; z <= maxZ; z++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (!IsBlocked(x, z))
                    continue;
                var cellMin = Origin + new Vector2(x, z) * CellSize - new Vector2(AgentRadius);
                var cellMax = cellMin + new Vector2(CellSize + 2f * AgentRadius);
                if (IntersectsBox(from, to, cellMin, cellMax))
                    return false;
            }
        }
        return true;
    }

    public bool TryFindSpawn(Vector2 requested, out Vector2 spawn)
    {
        spawn = requested;
        if (IsWalkable(requested))
            return true;
        var bestDistance = float.PositiveInfinity;
        for (var i = 0; i < CellCount; i++)
        {
            var candidate = Center(i);
            var distance = Vector2.DistanceSquared(requested, candidate);
            if (distance < bestDistance && IsWalkable(candidate))
            {
                bestDistance = distance;
                spawn = candidate;
            }
        }
        return float.IsFinite(bestDistance);
    }

    private bool InsideBounds(Vector2 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y) &&
        point.X >= Origin.X + AgentRadius && point.Y >= Origin.Y + AgentRadius &&
        point.X < Origin.X + Width * CellSize - AgentRadius &&
        point.Y < Origin.Y + Height * CellSize - AgentRadius;

    // Swept disk is conservatively tested against radius-expanded cell boxes.
    // Testing the full segment prevents tunneling, even if a tick spans several cells.
    private static bool IntersectsBox(Vector2 from, Vector2 to, Vector2 min, Vector2 max)
    {
        var direction = to - from;
        var enter = 0f;
        var exit = 1f;
        return ClipAxis(from.X, direction.X, min.X, max.X, ref enter, ref exit) &&
               ClipAxis(from.Y, direction.Y, min.Y, max.Y, ref enter, ref exit);
    }

    private static bool ClipAxis(float origin, float direction, float min, float max,
        ref float enter, ref float exit)
    {
        if (MathF.Abs(direction) < 0.000001f)
            return origin >= min && origin <= max;
        var first = (min - origin) / direction;
        var last = (max - origin) / direction;
        if (first > last)
            (first, last) = (last, first);
        enter = MathF.Max(enter, first);
        exit = MathF.Min(exit, last);
        return enter <= exit;
    }
}
