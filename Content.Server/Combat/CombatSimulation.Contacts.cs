using System.Numerics;
using Content.Server.Data;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly record struct BasicContact(Combatant Actor,AttackCommand Command,float Range,ulong Execution,
        Vector2 Origin,Vector2 Direction,Combatant? Target,PreparedDirectContact? Hit,uint Tick,bool EndWeapon,bool Basic=true,
        float OriginHeight=0,float DirectionY=0,float HalfAngle=0,float Thickness=0,BasicAttackDelivery Delivery=BasicAttackDelivery.MeleeArc);
    private readonly record struct ContactDeath(NetworkEntityId Actor,NetworkEntityId Source,bool UnknownSource);
    private readonly record struct AbilityContact(AbilityEffect Effect,Combatant Target,PreparedDirectContact Hit,uint Tick,bool Technique);
    private enum ContactKind:byte {Basic,Ability,Periodic}
    private readonly record struct ContactOrder(ulong Execution,NetworkEntityId Target,ContactKind Kind,int Index);
    private readonly List<BasicContact> _basicContacts=new();
    private readonly List<AbilityContact> _abilityContacts=new();
    private readonly List<ContactOrder> _contactOrder=new();
    private readonly Dictionary<ulong,ReleasedSourceSnapshot> _contactSources=new();
    private readonly HashSet<NetworkEntityId> _contactPulseBoundaries=new();
    private readonly List<ContactDeath> _contactDeaths=new();
    private readonly HashSet<NetworkEntityId> _contactDeadActors=new();
    private readonly HashSet<(NetworkEntityId Actor,ulong Execution)> _contactWeaponUses=new();
    private readonly List<(NetworkEntityId Actor,ulong Execution)> _contactWeaponEnds=new();
    private readonly HashSet<(NetworkEntityId Actor,ulong Execution)> _contactWeaponEndSet=new();
    private bool _capturingContacts,_resolvingContacts;
    internal bool CapturingContacts=>_capturingContacts;

    private void BeginContactBatch()
    {
        if(_capturingContacts || _resolvingContacts)throw new InvalidOperationException("Contact batches cannot nest.");
        _basicContacts.Clear();_contactDeaths.Clear();_contactDeadActors.Clear();_contactWeaponUses.Clear();_contactWeaponEnds.Clear();_contactWeaponEndSet.Clear();
        _abilityContacts.Clear();_contactOrder.Clear();_contactSources.Clear();_contactPulseBoundaries.Clear();
        _periodicContacts.Clear();
        _capturingContacts=true;
    }

    private void CaptureBasicContact(Combatant actor,AttackCommand command,WeaponDefinition weapon,ulong execution,uint tick,bool endWeapon)
    {
        var aim=BasicAttackShape.NormalizeDirection3D(command.Direction,command.DirectionY);
        var direction=new Vector2(aim.X,aim.Z);var range=(float)weapon.Range;
        var volume=BasicVolume(actor,weapon,aim,out var origin);
        _spatial.Query(actor.Position,range+MaximumHurtboxReach,_candidates);
        var focus=ConsumeFocus(actor.Id);
        var power=CombatBalanceMath.BasicPower(_calculator.WeaponPower(weapon,actor.Stats),IsSwordsman(actor.Id)?_catalog.Swordsman!.BasicDamageBonus:0,focus);
        var usesWeapon=(weapon.Capabilities&ActionCapability.Weapon)!=0;var rules=weapon.HitRules;
        var found=false;
        foreach(var id in _candidates)
        {
            if(!_actors.TryGetValue(id,out var target) || !CanTarget(actor.Id,id) || !BasicContactVisible(volume,origin,weapon,target))continue;
            found=true;
            _basicContacts.Add(new(actor,command,range,execution,actor.Position,direction,target,
                PrepareDirectContact(actor.Id,target.Id,direction,power,false,rules.Blockable,rules.Parryable,rules.PressureScale,
                    weaponExecution:usesWeapon?execution:0,execution:execution,context:weapon.HitContext),tick,endWeapon,OriginHeight:origin.Y,DirectionY:aim.Y,
                    HalfAngle:weapon.BasicVolume!.HalfAngleRadians,Thickness:weapon.BasicVolume.Thickness));
            _contactOrder.Add(new(execution,id,ContactKind.Basic,_basicContacts.Count-1));
        }
        if(!found)
        {
            _basicContacts.Add(new(actor,command,range,execution,actor.Position,direction,null,null,tick,endWeapon,
                OriginHeight:origin.Y,DirectionY:aim.Y,HalfAngle:weapon.BasicVolume!.HalfAngleRadians,Thickness:weapon.BasicVolume.Thickness));
            _contactOrder.Add(new(execution,default,ContactKind.Basic,_basicContacts.Count-1));
        }
        if(usesWeapon)_contactWeaponUses.Add((actor.Id,execution));
    }

    internal bool CaptureAbilityContact(AbilityEffect effect,Combatant target,double power,bool magic,uint tick,bool technique,double skillPower)
    {
        if(!_capturingContacts)return false;
        var rules=effect.Definition.HitRules;
        var prepared=PrepareDirectContact(effect.ActorId,target.Id,effect.Direction,power,magic,rules.Blockable,rules.Parryable,rules.PressureScale,
            effect.Profile.Id,effect.WeaponExecution,effect.HitSnapshot,effect.ReleasedSource,
            hitOrigin:effect.Profile.Form==AbilityForm.GroundArea ? effect.Position : null,
            projectile:effect.Profile.Form==AbilityForm.Projectile,execution:effect.Id,context:effect.Definition.HitContext,combo:effect.Combo);
        if(prepared is not {} hit)return true;
        if(effect.ReleasedSource is null)
        {
            if(!_contactSources.TryGetValue(effect.Id,out var source))
                _contactSources.Add(effect.Id,source=CaptureReleasedSource(effect.ActorId,skillPower));
            effect.ReleasedSource=source;
        }
        _abilityContacts.Add(new(effect,target,hit,tick,technique));
        _contactOrder.Add(new(effect.Id,target.Id,ContactKind.Ability,_abilityContacts.Count-1));
        if(effect.WeaponExecution!=0)_contactWeaponUses.Add((effect.ActorId,effect.WeaponExecution));
        if(effect.WeaponExecution!=0 && effect.Definition.Toggle is {OccupiesAction:false})_contactPulseBoundaries.Add(effect.ActorId);
        return true;
    }

    private void ResolveContactBatch()
    {
        _capturingContacts=false;_resolvingContacts=true;
        try
        {
            _contactOrder.Sort(static (left,right)=>
            {
                var order=left.Execution.CompareTo(right.Execution);if(order!=0)return order;
                order=left.Target.Value.CompareTo(right.Target.Value);if(order!=0)return order;
                order=((byte)left.Kind).CompareTo((byte)right.Kind);return order!=0?order:left.Index.CompareTo(right.Index);
            });
            foreach(var order in _contactOrder)
            {
                if(order.Kind==ContactKind.Periodic){ResolvePeriodicContact(_periodicContacts[order.Index]);continue;}
                if(order.Kind==ContactKind.Ability)
                {
                    var ability=_abilityContacts[order.Index];var outcome=ResolvePreparedContact(ability.Hit);
                    InputAbilities!.CompleteContact(ability.Effect,ability.Target,ability.Tick,outcome,ability.Technique);continue;
                }
                var contact=_basicContacts[order.Index];
                var hit=default(ResolvedHit);
                if(contact.Hit is {} prepared)hit=ResolvePreparedContact(prepared);
                if(contact.Basic)SwordHit(contact.Actor.Id,basic:true,success:hit.Damage>0);
                _events.Add(new(contact.Actor.Id,contact.Command.Sequence,contact.Tick,contact.Origin,contact.Direction,contact.Range,
                    contact.Target?.Id??NetworkEntityId.Invalid,hit.Damage,contact.Target?.Health??0,hit.Critical,hit.Guard,contact.Execution,hit.Evaded,
                    contact.DirectionY,contact.OriginHeight,contact.HalfAngle,contact.Thickness,hit.Tags,contact.Delivery));
                if(contact.EndWeapon)
                {EndWeaponUse(contact.Actor.Id,contact.Execution);SettleWeaponBoundary(contact.Actor.Id);}
            }
            InputAbilities?.FinishContactTechniques();
            // A pulse released during input admission has only now dealt its HP damage.
            // Settle D068 before ability presentation is built, including an activation pulse.
            foreach(var actor in _contactPulseBoundaries)SettleWeaponPulseBoundary(actor);
        }
        finally{_capturingContacts=false;_resolvingContacts=false;}
        // Wear must see every fixed contact before death/drop can move its item instance.
        foreach(var (actor,execution) in _contactWeaponEnds)WeaponExecutionFinished?.Invoke(actor,execution);
        foreach(var (actor,_) in _contactWeaponEnds)SettleWeaponBoundary(actor);
        foreach(var death in _contactDeaths)if(_actors.ContainsKey(death.Actor))CompleteDeath(death.Actor,death.Source,death.UnknownSource);
        _basicContacts.Clear();_contactDeaths.Clear();_contactDeadActors.Clear();_contactWeaponUses.Clear();_contactWeaponEnds.Clear();_contactWeaponEndSet.Clear();
        _abilityContacts.Clear();_contactOrder.Clear();_contactSources.Clear();_contactPulseBoundaries.Clear();
        _periodicContacts.Clear();
    }

    private bool DeferContactDeath(NetworkEntityId actor,NetworkEntityId source,bool unknownSource)
    {
        if(!_resolvingContacts)return false;
        if(_contactDeadActors.Add(actor))_contactDeaths.Add(new(actor,source,unknownSource));
        return true;
    }

    private bool DeferContactWeaponEnd(NetworkEntityId actor,ulong execution)
    {
        if(!(_capturingContacts || _resolvingContacts) || !_contactWeaponUses.Contains((actor,execution)))return false;
        if(_contactWeaponEndSet.Add((actor,execution)))_contactWeaponEnds.Add((actor,execution));return true;
    }
}
