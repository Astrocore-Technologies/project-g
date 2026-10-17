using System.Numerics;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly Dictionary<ulong,ProjectileContact> _projectileContacts=new();

    private static ProjectilePath ProjectilePathFor(AbilityEffect effect)=>new(
        new(effect.Origin.X,effect.OriginHeight,effect.Origin.Y),new(effect.Direction.X,effect.DirectionY,effect.Direction.Y),
        effect.Profile.Speed,effect.Profile.Radius,effect.Profile.Range,effect.ProjectileStartedAt,effect.ProjectileOffset,effect.ProjectileEndsAt);

    private Vector3 ProjectilePoint(AbilityEffect effect,double distance)
    {
        var velocity=ProjectileVelocity(effect);var elapsed=distance/effect.Profile.Speed;
        return new(effect.Origin.X+(float)(velocity.X*elapsed),effect.OriginHeight+(float)(velocity.Y*elapsed),
            effect.Origin.Y+(float)(velocity.Z*elapsed));
    }
    private static Vector3 ProjectileVelocity(AbilityEffect effect)=>new Vector3(effect.Direction.X,effect.DirectionY,effect.Direction.Y)*effect.Profile.Speed;
    private double ProjectileDistance(AbilityEffect effect,double at)=>at>=effect.ProjectileEndsAt ? effect.Profile.Range : Math.Min(effect.Profile.Range,
        effect.ProjectileOffset+Math.Max(0,at-effect.ProjectileStartedAt)*effect.Profile.Speed);

    private double PlanProjectiles(double end)
    {
        _projectileContacts.Clear();var next=end;
        foreach(var effect in _effects)
        {
            if(effect.Phase!=AbilityPhase.Flying)continue;
            var seconds=Math.Min(end-_time,Math.Max(0,effect.ProjectileEndsAt-_time));
            // These are spatial-query windows, not a range/speed limit. A longer flight
            // continues through as many windows as necessary in the same world tick.
            var radius=effect.Profile.Radius+_combat.MaximumHurtboxReach;
            if(radius>=_queryLimit)throw new InvalidOperationException("Projectile radius exceeds the spatial query budget.");
            seconds=Math.Min(seconds,120d/effect.Profile.Speed);
            seconds=Math.Min(seconds,(_queryLimit-radius)/(effect.Profile.Speed*.5+_combat.MaximumTargetSpeed));
            var contact=TraceProjectile(effect,seconds,movingTargets:true);
            _projectileContacts.Add(effect.Id,contact);
            next=Math.Min(next,contact.At);
        }
        return next;
    }

    private ProjectileContact TraceProjectile(AbilityEffect effect,double seconds,bool movingTargets)
        =>_combat.TraceProjectile(ProjectilePathFor(effect),effect.ActorId,effect.ReleasedSource,_time,seconds,movingTargets);

    private void SetProjectilePosition(ref AbilityEffect effect,double distance)
    {
        var point=ProjectilePoint(effect,distance);effect.Position=new(point.X,point.Z);effect.Height=point.Y;
        effect.DistanceLeft=(float)Math.Max(0,effect.Profile.Range-distance);effect.Remaining=effect.DistanceLeft/effect.Profile.Speed;
    }

    private void ReleaseProjectile(ref AbilityEffect effect,uint tick)
    {
        effect.ProjectileStartedAt=_time;effect.ProjectileOffset=0;effect.ProjectileEndsAt=_time+effect.Profile.Range/effect.Profile.Speed;
        if(!double.IsFinite(effect.ProjectileEndsAt) || effect.ProjectileEndsAt<=_time)
            throw new InvalidOperationException("Projectile flight cannot be represented on the combat clock.");
        var contact=TraceProjectile(effect,0,movingTargets:false);
        var distance=Math.Min(effect.Profile.Range,effect.Profile.Speed*(contact.At-_time));
        effect.ProjectileOffset=distance;effect.ProjectileEndsAt=_time+(effect.Profile.Range-distance)/effect.Profile.Speed;
        SetProjectilePosition(ref effect,distance);
        if(contact.Target.IsValid && _combat.TryGet(contact.Target,out var target))Hit(effect,target,tick);
        if(contact.Target.IsValid || contact.Obstacle || distance>=effect.Profile.Range)effect.Phase=AbilityPhase.Finished;
    }

    private void AdvanceProjectile(ref AbilityEffect effect,uint tick)
    {
        SetProjectilePosition(ref effect,ProjectileDistance(effect,_time));
        if(_projectileContacts.TryGetValue(effect.Id,out var contact))
        {
            if(contact.HurtboxRevision!=_combat.HurtboxRevision)
            {
                // Control expiry/landing changes shape before contacts at this timestamp.
                // Requery only that exceptional boundary, not every motion substep.
                var fresh=TraceProjectile(effect,0,movingTargets:false);
                var keepOld=contact.At<=_time && (contact.Obstacle || contact.Target.IsValid && _combat.TryGet(contact.Target,out var oldTarget) &&
                    contact.Prone==(oldTarget.Control==CombatControlPhase.KnockedDown));
                if(fresh.Obstacle || fresh.Target.IsValid && (!keepOld || fresh.Target.Value<contact.Target.Value))contact=fresh;
                else if(!keepOld)contact=new(_time,default,false,_combat.HurtboxRevision);
            }
            if(contact.At<=_time)
            {
                if(contact.Obstacle){effect.Phase=AbilityPhase.Finished;return;}
                if(contact.Target.IsValid && _combat.TryGet(contact.Target,out var target) && target.Health>0 && CanProjectileTarget(effect,target.Id))
                {Hit(effect,target,tick);effect.Phase=AbilityPhase.Finished;return;}
            }
        }
        if(effect.DistanceLeft<=0)effect.Phase=AbilityPhase.Finished;
    }
}
