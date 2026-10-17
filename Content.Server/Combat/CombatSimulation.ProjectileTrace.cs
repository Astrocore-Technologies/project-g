using System.Numerics;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal ProjectileContact TraceProjectile(in ProjectilePath path,NetworkEntityId source,ReleasedSourceSnapshot? released,
        double now,double seconds,bool movingTargets)
    {
        var distance=path.Distance(now);var from=path.Point(distance);
        var to=path.Point(Math.Min(path.Range,distance+path.Speed*seconds));
        var obstacle=_navigation.TryProjectileObstacle(from,to,path.Radius,out var wallFraction);
        var first=now+(obstacle?seconds*wallFraction:seconds);var target=default(NetworkEntityId);
        var midpoint=(new Vector2(from.X,from.Z)+new Vector2(to.X,to.Z))*.5f;
        var radius=path.Radius+MaximumHurtboxReach;
        var queryRadius=Vector2.Distance(new(from.X,from.Z),new(to.X,to.Z))*.5+radius+
            (movingTargets?MaximumTargetSpeed*seconds:0);
        _spatial.Query(midpoint,(float)Math.Min(_maxQueryRange,queryRadius),_candidates);
        foreach(var id in _candidates)
        {
            if(!_actors.TryGetValue(id,out var candidate) || candidate.Health<=0 ||
                (released is not null ? !CanReleasedTarget(released,id) : !CanTarget(source,id)))continue;
            var motion=movingTargets?CollisionMotion(id):default;
            if(!ProjectileContactTime(path,candidate,motion,now,seconds,out var hit) ||
                hit>first || hit==first && (obstacle || target.IsValid && id.Value>=target.Value))continue;
            first=hit;target=id;obstacle=false;
        }
        return new(first,target,obstacle,HurtboxRevision,
            target.IsValid && _actors[target].Control==CombatControlPhase.KnockedDown);
    }

    private static bool ProjectileContactTime(in ProjectilePath path,Combatant target,CombatMotion motion,double now,double seconds,out double at)
    {
        var distance=path.Distance(now);var capsule=target.Hurtbox;var center=capsule.Center;var velocity=path.Velocity;
        var elapsedDistance=distance/path.Speed;
        var x=(double)path.Origin.X+velocity.X*elapsedDistance-center.X;
        var y=(double)path.Origin.Y+velocity.Y*elapsedDistance-center.Y;
        var z=(double)path.Origin.Z+velocity.Z*elapsedDistance-center.Z;
        at=now;
        var radius=capsule.Radius+path.Radius;
        if(capsule.DistanceSquared(x,y,z)<=radius*radius)return true;
        if(motion==default)
        {
            var offsetSeconds=path.Offset/path.Speed;
            x=(double)path.Origin.X+velocity.X*offsetSeconds-center.X;
            y=(double)path.Origin.Y+velocity.Y*offsetSeconds-center.Y;
            z=(double)path.Origin.Z+velocity.Z*offsetSeconds-center.Z;
            if(!ProjectileCollision.TryCapsuleTime(x,y,z,velocity,Vector3.Zero,capsule,path.Radius,now+seconds-path.StartedAt,out var elapsed))return false;
            at=path.StartedAt+elapsed;return at>=now;
        }
        if(!ProjectileCollision.TryCapsuleTime(x,y,z,velocity-motion.Velocity,-motion.Acceleration,capsule,path.Radius,seconds,out var hit))return false;
        at=now+hit;return true;
    }
}
