using Content.Shared.Navigation;
using Godot;
using ProjectG.Networking;
using V2 = System.Numerics.Vector2;

namespace ProjectG.Regions;

/// <summary>Presentation of legacy public X/Z anchors on the lowest authored floor.</summary>
public static class PublicSurfaceAnchors
{
    public static Vector3 Position(NetworkClient network, V2 point, float offset = 0)
    {
        var height = 0f;
        if (network.Navigation?.Surface is { } surface)
        {
            var column = new List<SurfaceLocation>();
            surface.SampleColumn(point.X, point.Y, column);
            if (column.Count > 0)
            {
                height = column[0].Position.Y;
                for (var i = 1; i < column.Count; i++) height = MathF.Min(height, column[i].Position.Y);
            }
        }
        return new(point.X, height+offset, point.Y);
    }
}
