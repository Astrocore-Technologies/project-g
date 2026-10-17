using System.Numerics;
using Content.Server.Data;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private static MeleeArc BasicVolume(Combatant actor,WeaponDefinition weapon,Vector3 direction,out Vector3 origin)
    {
        var profile=weapon.BasicVolume??throw new InvalidOperationException("Melee basic has no authored volume.");
        origin=actor.Foot+Vector3.UnitY*profile.OriginHeight;
        return new(origin,direction,actor.Facing,weapon.Range,profile.ArcDegrees*Math.PI/180,profile.Thickness);
    }

    private bool BasicContactVisible(MeleeArc volume,Vector3 origin,WeaponDefinition weapon,Combatant target)
    {
        var body=target.Hurtbox;
        if(!volume.TryContact(body,out var blade))return false;
        return BodyContactVisible(origin,blade,weapon.BasicVolume!.Thickness*.5f,body);
    }

    internal bool BodyContactVisible(Vector3 origin,Vector3 blade,float radius,CombatCapsule body)
    {
        var delta=blade-origin;
        // A witness lies on a radial blade path inside the swept volume. Stop the
        // world trace at its first body contact, so a wall behind the body cannot hide it.
        var offset=origin-body.Center;
        // Geometry already proved this witness. At a tangent, conversion of the
        // analytic point to float can put it one ulp outside the CCD surface.
        // In that case the world trace still ends at the proven contact itself.
        if(!ProjectileCollision.TryCapsuleTime(offset.X,offset.Y,offset.Z,delta,Vector3.Zero,body,radius,1,out var fraction))fraction=1;
        var end=origin+delta*(float)fraction;var distance=Vector3.Distance(origin,end);
        var windows=Math.Max(1,(int)Math.Ceiling(distance/120));
        for(var step=0;step<windows;step++)
        {
            var start=Vector3.Lerp(origin,end,(float)step/windows);var finish=Vector3.Lerp(origin,end,(float)(step+1)/windows);
            if(radius==0 ? _navigation.TryVisibilityObstacle(start,finish,out _) :
                _navigation.TryProjectileObstacle(start,finish,radius,out _))return false;
        }
        return true;
    }
}
