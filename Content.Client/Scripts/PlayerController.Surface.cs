using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace ProjectG.Gameplay;

public partial class PlayerController
{
    private bool TryCursorDirection(out NumericsVector2 point, Vector2? screenPoint = null)
    {
        point = default;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return false;
        var mouse = screenPoint ?? GetViewport().GetMousePosition();
        var origin = camera.ProjectRayOrigin(mouse); var ray = camera.ProjectRayNormal(mouse);
        if (Mathf.Abs(ray.Y) < .0001f) return false;
        var distance = (_predictedHeight - origin.Y) / ray.Y;
        if (distance <= 0) return false;
        var hit = origin + ray * distance;
        point = new(hit.X, hit.Z); return true;
    }
    internal ProjectG.Combat.CombatAimBody? CombatAimBody {get;set;}

    public bool TryCursorCombatPoint(out NumericsVector2 point,out float height,Vector2? screenPoint=null)
    {
        point=default;height=0;
        var camera=GetViewport().GetCamera3D();if(camera is null)return false;
        var mouse=screenPoint??GetViewport().GetMousePosition();var origin=camera.ProjectRayOrigin(mouse);var ray=camera.ProjectRayNormal(mouse);
        var excluded=new Godot.Collections.Array<Rid>{GetRid()};
        if(CombatAimBody is {} body && GodotObject.IsInstanceValid(body))excluded.Add(body.GetRid());
        var query=PhysicsRayQueryParameters3D.Create(origin,origin+ray*500,1|ProjectG.Combat.CombatAimBody.Layer,excluded);
        query.CollideWithAreas=true;
        var result=GetWorld3D().DirectSpaceState.IntersectRay(query);
        if(result.Count>0)
        {var hit=result["position"].AsVector3();point=new(hit.X,hit.Z);height=hit.Y;return true;}
        return TryCursorSurface(out point,out height,screenPoint);
    }

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
        // The outdoor bake samples half-metre cells. Project a physical terrain
        // hit onto its nearby floor, without selecting a distant bridge/other storey.
        var tolerance = result["collider"].AsGodotObject() is Node collider && collider.IsInGroup("surface_terrain") ? .5f : .35f;
        if (!_navigation.Surface.TryLocate(new(position.X, position.Y, position.Z), tolerance, out var located)) return false;
        point = new(located.Position.X, located.Position.Z); height = located.Position.Y; return true;
    }
}
