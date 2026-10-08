using System.Numerics;
using Content.Shared.Combat;

namespace Content.Shared.Navigation;

/// <summary>Shared straight-line clipping: a dash never routes around or crosses a wall.</summary>
public static class DashGeometry
{
    public static bool TryDestination(NavigationGrid grid, Vector2 origin, Vector2 direction, float range, out Vector2 destination)
    {
        destination = origin;
        if (!BasicAttackShape.IsValidDirection(direction) || !float.IsFinite(range) || range <= 0 || !grid.IsWalkable(origin))
            return false;
        direction = Vector2.Normalize(direction);
        var end = origin + direction * range;
        if (grid.CanTraverse(origin, end)) { destination = end; return true; }
        var low = 0f;
        var high = range;
        // Predicate tests the full prefix, so it remains monotonic even with multiple walls.
        for (var i = 0; i < 16; i++)
        {
            var middle = (low + high) * 0.5f;
            if (grid.CanTraverse(origin, origin + direction * middle)) low = middle;
            else high = middle;
        }
        destination = origin + direction * low;
        return low > 0.01f;
    }
}
