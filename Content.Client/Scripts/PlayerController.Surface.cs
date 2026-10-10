using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace ProjectG.Gameplay;

public partial class PlayerController
{
    public bool TryCursorSurface(out NumericsVector2 point, out float height, Vector2? screenPoint = null)
    {
        point = default; height = 0;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return false;
        var mouse = screenPoint ?? GetViewport().GetMousePosition(); var origin = camera.ProjectRayOrigin(mouse); var ray = camera.ProjectRayNormal(mouse);
        if (_navigation.Surface is null)
        {
            if (Mathf.Abs(ray.Y) < .0001f || -origin.Y / ray.Y <= 0) return false;
            var hit = origin + ray * (-origin.Y / ray.Y); point = new(hit.X, hit.Z); return true;
        }
        // Input selects actual collision geometry, including a bridge deck or a sunken floor.
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + ray * 500, 1,
            new Godot.Collections.Array<Rid> { GetRid() });
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0) return false;
        var position = result["position"].AsVector3();
        if (!_navigation.Surface.TryLocate(new(position.X, position.Y, position.Z), .35f, out var located)) return false;
        point = new(located.Position.X, located.Position.Z); height = located.Position.Y; return true;
    }
}
