using Content.Shared.Navigation;
using Godot;

namespace ProjectG.Navigation;

/// <summary>Prototype obstacle display built from server geometry, not a second collision definition.</summary>
public partial class NavigationVisual : Node3D
{
    public void Build(NavigationGrid grid)
    {
        var size = new Vector3(grid.CellSize, 2f, grid.CellSize);
        var mesh = new BoxMesh { Size = size };
        var shape = new BoxShape3D { Size = size };
        var material = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.5f, 0.65f) };
        for (var z = 0; z < grid.Height; z++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (!grid.IsBlocked(x, z))
                    continue;
                var center = grid.Center(z * grid.Width + x);
                var body = new StaticBody3D { Position = new Vector3(center.X, 1f, center.Y) };
                body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material });
                body.AddChild(new CollisionShape3D { Shape = shape });
                AddChild(body);
            }
        }
    }
}
