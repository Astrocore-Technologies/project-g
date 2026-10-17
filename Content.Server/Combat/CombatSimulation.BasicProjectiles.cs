using System.Numerics;
using Content.Server.Data;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private sealed class BasicProjectile(Combatant actor,AttackCommand command,WeaponDefinition weapon,ulong execution,
        ProjectilePath path,ReleasedSourceSnapshot source,DirectHitSnapshot offense)
    {
        internal readonly Combatant Actor=actor;
        internal readonly AttackCommand Command=command;
        internal readonly WeaponDefinition Weapon=weapon;
        internal readonly ulong Execution=execution;
        internal readonly ProjectilePath Path=path;
        internal readonly ReleasedSourceSnapshot Source=source;
        internal readonly DirectHitSnapshot Offense=offense;
    }
    private readonly Dictionary<ulong,BasicProjectile> _basicProjectiles=new();
    private readonly Dictionary<ulong,ProjectileContact> _basicProjectileContacts=new();
    private readonly List<ulong> _finishedBasicProjectiles=new();

    private void ReleaseBasicProjectile(Combatant actor,AttackCommand command,WeaponDefinition weapon,ulong execution,uint tick,bool endWeapon)
    {
        var profile=weapon.BasicProjectile!;
        var direction=BasicAttackShape.NormalizeDirection3D(command.Direction,command.DirectionY);
        var origin=actor.Foot+Vector3.UnitY*profile.OriginHeight;
        var end=_time+weapon.Range/profile.Speed;
        if(!double.IsFinite(end) || end<=_time || !float.IsFinite((float)(end-_time)))
            throw new InvalidOperationException("Basic projectile flight cannot be represented on the combat clock.");
        var power=CombatBalanceMath.BasicPower(_calculator.WeaponPower(weapon,actor.Stats),
            IsSwordsman(actor.Id)?_catalog.Swordsman!.BasicDamageBonus:0,ConsumeFocus(actor.Id));
        var projectile=new BasicProjectile(actor,command,weapon,execution,
            new(origin,direction,profile.Speed,profile.Radius,weapon.Range,_time,0,end),CaptureReleasedSource(actor.Id,1),CaptureDirectHit(actor.Id,power));
        var contact=TraceProjectile(projectile.Path,actor.Id,projectile.Source,_time,0,false);
        if(contact.Target.IsValid || contact.Obstacle)
            CaptureBasicProjectileContact(projectile,contact.Target,tick,endWeapon);
        else _basicProjectiles.Add(execution,projectile);
    }

    private double PlanBasicProjectiles(double end)
    {
        _basicProjectileContacts.Clear();var next=end;
        foreach(var (execution,projectile) in _basicProjectiles)
        {
            var path=projectile.Path;
            var seconds=Math.Min(end-_time,Math.Max(0,path.EndsAt-_time));
            var radius=path.Radius+MaximumHurtboxReach;
            seconds=Math.Min(seconds,120d/path.Speed);
            seconds=Math.Min(seconds,(_maxQueryRange-radius)/(path.Speed*.5+MaximumTargetSpeed));
            var contact=TraceProjectile(path,projectile.Actor.Id,projectile.Source,_time,seconds,true);
            _basicProjectileContacts.Add(execution,contact);next=Math.Min(next,contact.At);
        }
        return next;
    }

    private void AdvanceBasicProjectiles(uint tick)
    {
        _finishedBasicProjectiles.Clear();
        foreach(var (execution,projectile) in _basicProjectiles)
        {
            var path=projectile.Path;var finished=path.Distance(_time)>=path.Range;var target=default(NetworkEntityId);
            if(_basicProjectileContacts.TryGetValue(execution,out var contact))
            {
                if(contact.HurtboxRevision!=HurtboxRevision)
                {
                    var fresh=TraceProjectile(path,projectile.Actor.Id,projectile.Source,_time,0,false);
                    var keepOld=contact.At<=_time && (contact.Obstacle || contact.Target.IsValid && _actors.TryGetValue(contact.Target,out var old) &&
                        contact.Prone==(old.Control==CombatControlPhase.KnockedDown));
                    if(fresh.Obstacle || fresh.Target.IsValid && (!keepOld || fresh.Target.Value<contact.Target.Value))contact=fresh;
                    else if(!keepOld)contact=new(_time,default,false,HurtboxRevision);
                }
                if(contact.At<=_time)
                {
                    if(contact.Obstacle)finished=true;
                    else if(contact.Target.IsValid && CanReleasedTarget(projectile.Source,contact.Target))
                    {target=contact.Target;finished=true;}
                }
            }
            if(!finished)continue;
            var endWeapon=!_basicActions.TryGetValue(projectile.Actor.Id,out var action) || action.Action.ExecutionId!=execution;
            CaptureBasicProjectileContact(projectile,target,tick,endWeapon);
            _finishedBasicProjectiles.Add(execution);
        }
        foreach(var execution in _finishedBasicProjectiles)_basicProjectiles.Remove(execution);
    }

    private void CaptureBasicProjectileContact(BasicProjectile projectile,NetworkEntityId targetId,uint tick,bool endWeapon)
    {
        var path=projectile.Path;var direction=new Vector2(path.Direction.X,path.Direction.Z);var weapon=projectile.Weapon;
        _actors.TryGetValue(targetId,out var target);var rules=weapon.HitRules;
        var usesWeapon=(weapon.Capabilities&ActionCapability.Weapon)!=0;
        var hit=target is not null ? PrepareDirectContact(projectile.Actor.Id,targetId,direction,projectile.Offense.Power,
            weapon.BasicProjectile!.DamageFamily==EffectDamageFamily.Magical,rules.Blockable,rules.Parryable,rules.PressureScale,
            weaponExecution:usesWeapon?projectile.Execution:0,snapshot:projectile.Offense,releasedSource:projectile.Source,
            projectile:true,execution:projectile.Execution,context:projectile.Weapon.HitContext) : null;
        _basicContacts.Add(new(projectile.Actor,projectile.Command,(float)path.Range,projectile.Execution,new(path.Origin.X,path.Origin.Z),direction,
            target,hit,tick,endWeapon,Basic:false,OriginHeight:path.Origin.Y,DirectionY:path.Direction.Y,Delivery:BasicAttackDelivery.Projectile));
        _contactOrder.Add(new(projectile.Execution,targetId,ContactKind.Basic,_basicContacts.Count-1));
        if(usesWeapon)_contactWeaponUses.Add((projectile.Actor.Id,projectile.Execution));
    }

    internal void PublishBasicProjectiles(List<AbilityEffectState> states,uint tick)
    {
        foreach(var projectile in _basicProjectiles.Values)
        {
            var path=projectile.Path;var point=path.Point(path.Distance(_time));
            states.Add(new(projectile.Execution,projectile.Actor.Id,projectile.Command.Sequence,tick,0,AbilityForm.Projectile,AbilityPhase.Flying,
                new(path.Origin.X,path.Origin.Z),new(point.X,point.Z),new(path.Direction.X,path.Direction.Z),path.Radius,path.Speed,
                (float)Math.Max(0,path.EndsAt-_time),path.Origin.Y,point.Y,path.Direction.Y,new(AbilityAreaShape.Corridor,(float)path.Range,path.Radius)));
        }
    }
}
