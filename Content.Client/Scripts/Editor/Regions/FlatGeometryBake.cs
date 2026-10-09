using Content.Shared.Navigation;
using Content.Shared.Network;
using Godot;

namespace ProjectG.Regions.Authoring;

/// <summary>Conservative, deterministic one-metre raster for the flat demo only.</summary>
public static class FlatGeometryBake
{
    public static FlatRegionGeometry Bake(RegionRoot root)
    {
        var errors = RegionAuthoringValidation.Validate(root);
        if (errors.Count != 0) throw new InvalidDataException(string.Join("; ", errors));
        var bounds = root.MovementBounds;
        var width = (int)bounds.Size.X;
        var height = (int)bounds.Size.Y;
        if (width != bounds.Size.X || height != bounds.Size.Y || (long)width * height > NetworkConstants.MaxNavigationCells)
            throw new InvalidDataException("Flat export requires bounded whole-metre dimensions.");
        var floors = new List<Rect2>();
        var blockers = new List<Rect2>();
        var patches = new List<Rect2>();
        var openingCells = new List<int>();
        Visit(root, Transform3D.Identity);
        var cells = new byte[width * height];
        for (var z = 0; z < height; z++)
        for (var x = 0; x < width; x++)
        {
            var cell = new Rect2(bounds.Position + new Vector2(x, z), Vector2.One);
            // Full cell support; radius is applied once by shared NavigationGrid.
            var supported = floors.Any(f => f.Encloses(cell)) && !blockers.Any(b => b.Intersects(cell));
            var patched = patches.Any(b => b.Intersects(cell));
            cells[z * width + x] = (byte)(supported && !patched ? 0 : 1);
            if (supported && patched) openingCells.Add(z * width + x);
        }
        return FlatRegionGeometry.FromGrid(new NavigationGrid(new RegionNavigation(
            new(bounds.Position.X, bounds.Position.Y), 1, .45f, (ushort)width, (ushort)height, cells))) with { OpeningCells = openingCells.ToArray() };

        void Visit(Node parent, Transform3D transform)
        {
            foreach (var child in parent.GetChildren())
            {
                var local = child is Node3D spatial ? transform * spatial.Transform : transform;
                if (child is CollisionShape3D { Disabled: false } collision && collision.GetParent() is StaticBody3D body)
                {
                    var floor = body.IsInGroup("region_walkable");
                    var blocker = body.IsInGroup("region_movement_blocker");
                    if (!floor && !blocker) throw new InvalidDataException($"Unclassified collision: {body.Name}");
                    if (collision.Shape is not BoxShape3D box || !local.Basis.IsEqualApprox(Basis.Identity))
                        throw new InvalidDataException($"Flat export requires axis-aligned boxes: {body.Name}");
                    if (floor && !Mathf.IsZeroApprox(local.Origin.Y + box.Size.Y / 2))
                        throw new InvalidDataException($"Walkable floor must be at Y=0: {body.Name}");
                    var rectangle = new Rect2(local.Origin.X - box.Size.X / 2, local.Origin.Z - box.Size.Z / 2, box.Size.X, box.Size.Z);
                    (floor ? floors : body.IsInGroup("region_repair_barrier") ? patches : blockers).Add(rectangle);
                }
                Visit(child, local);
            }
        }
    }
}
