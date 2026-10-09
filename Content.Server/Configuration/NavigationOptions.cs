using System.Numerics;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.Configuration;

/// <summary>Prototype flat geometry. Blocking rectangles are expressed in grid cells.</summary>
public sealed class NavigationOptions
{
    public const string SectionName = "Navigation";
    public string? GeometryFile { get; init; }
    public float CellSize { get; init; } = 1f;
    public float AgentRadius { get; init; } = 0.45f;
    public List<BlockedAreaOptions> BlockedAreas { get; init; } = new();

    public NavigationGrid CreateGrid(MovementSettings movement)
    {
        if (GeometryFile is not null)
        {
            var exported = FlatRegionGeometry.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, GeometryFile))).CreateGrid();
            if (exported.Origin.X != movement.MinX || exported.Origin.Y != movement.MinZ ||
                exported.Origin.X + exported.Width * exported.CellSize != movement.MaxX || exported.Origin.Y + exported.Height * exported.CellSize != movement.MaxZ)
                throw new InvalidDataException("Exported geometry does not match movement bounds.");
            return exported;
        }
        if (!float.IsFinite(CellSize) || CellSize is < 0.1f or > 100f)
            throw new ArgumentException("Navigation cell size must be between 0.1 and 100.");
        var width = (movement.MaxX - movement.MinX) / CellSize;
        var height = (movement.MaxZ - movement.MinZ) / CellSize;
        if (!float.IsFinite(width) || !float.IsFinite(height) ||
            width < 1f || height < 1f || width * height > NetworkConstants.MaxNavigationCells ||
            MathF.Abs(width - MathF.Round(width)) > 0.0001f ||
            MathF.Abs(height - MathF.Round(height)) > 0.0001f)
            throw new ArgumentException("Movement bounds must fit a bounded, whole-cell navigation grid.");
        var columns = (ushort) MathF.Round(width);
        var rows = (ushort) MathF.Round(height);
        var blocked = new byte[columns * rows];
        foreach (var area in BlockedAreas)
        {
            if (area.X < 0 || area.Z < 0 || area.Width <= 0 || area.Height <= 0 ||
                area.X >= columns || area.Z >= rows ||
                area.Width > columns - area.X || area.Height > rows - area.Z)
                throw new ArgumentException("Blocked rectangle is outside the navigation grid.");
            for (var z = area.Z; z < area.Z + area.Height; z++)
            {
                for (var x = area.X; x < area.X + area.Width; x++)
                    blocked[z * columns + x] = 1;
            }
        }
        var grid = new NavigationGrid(new RegionNavigation(
            new Vector2(movement.MinX, movement.MinZ), CellSize, AgentRadius, columns, rows, blocked));
        if (!grid.TryFindSpawn(grid.Origin, out _))
            throw new ArgumentException("Navigation grid has no valid spawn point.");
        return grid;
    }
}
