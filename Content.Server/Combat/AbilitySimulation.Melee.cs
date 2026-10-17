using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    internal Action<NetworkEntityId>? StopForCast { get; set; }

    private AbilityProfile ExecutionProfile(NetworkEntityId id, AbilityActor actor, int index)
    {
        var profile = actor.Profiles[index]; var definition = actor.Definitions[index];
        if(MissingBackgroundToggleWeapon(id,definition))profile=profile with {Availability=AbilityAvailability.NeedsWeapon};
        if (definition.Melee is { } technique)
            profile = profile with
            {
                Range = (float)_combat.Get(id).Weapon.Range * technique.RangeFactor,
                Availability = !_combat.HasSword(id) ? AbilityAvailability.NeedsSword : technique.RequiresParry && !_combat.HasRiposte(id) ? AbilityAvailability.NeedsParry : AbilityAvailability.Ready
            };
        return profile with { Area = DescribeArea(definition, profile),CooldownSeconds=Content.Server.Stats.StatCalculator.CooldownDuration(profile.CooldownSeconds,_combat.Get(id).Stats) };
    }

    internal void CancelRecovery(NetworkEntityId id) => StopActions(id,ActionStopReason.Voluntary,recoveryOnly:true);
    internal void InterruptCast(NetworkEntityId id) => StopActions(id,ActionStopReason.EnemyInterrupt);
    internal bool CancelAction(NetworkEntityId id,uint relatedSequence,ulong executionId=0) => StopActions(id,ActionStopReason.Voluntary,relatedSequence:relatedSequence,executionId:executionId,includeBasic:false);

    private bool ResolveTechnique(AbilityEffect effect, uint tick)
    {
        var technique = effect.Definition.Melee!;
        if (!_combat.HasSword(effect.ActorId)) return false;
        if (effect.Profile.Form == AbilityForm.Recovery)
        {
            _combat.RestoreStamina(effect.ActorId, technique.RestoreStamina);
            _practice.Add((effect.ActorId, effect.Profile.Id)); return true;
        }
        var actor = _combat.Get(effect.ActorId);
        _spatial.Query(actor.Position, effect.Profile.Range + _combat.MaximumHurtboxReach, _candidates);
        Combatant? first = null; var distance = double.PositiveInfinity;
        foreach (var id in _candidates)
        {
            if (!MeleeCandidate(effect, actor, id, out var target,out var progress)) continue;
            var next = technique.FirstTargetOnly ? progress : Vector3.DistanceSquared(actor.Foot,target.Foot);
            if (next < distance || next == distance && (first is null || id.Value < first.Id.Value)) { first = target; distance = next; }
        }
        if (first is null) return false;
        var hit = TechniqueHit(effect, first, tick, effect.FocusFactor);
        if (technique.FirstTargetOnly) return hit;
        foreach (var id in _candidates)
            if (id != first.Id && MeleeCandidate(effect, actor, id, out var target,out _)) hit |= TechniqueHit(effect, target, tick, 1);
        return hit;
    }

    private bool MeleeCandidate(AbilityEffect effect, Combatant actor, NetworkEntityId id, out Combatant target,out double progress)
    {
        progress=0;
        if (!_combat.TryGet(id, out target!) || !_combat.CanTarget(actor.Id, id)) return false;
        var t = effect.Definition.Melee!;
        var direction=new Vector3(effect.Direction.X,effect.DirectionY,effect.Direction.Y);
        var origin=actor.Foot+Vector3.UnitY*effect.Profile.MeleeOriginHeight;
        Vector3 blade;bool hit;float radius;
        if (t.NarrowThrust)
        {
            radius=effect.Profile.Radius;
            hit=new MeleeThrust(origin,direction,effect.Profile.Range,radius).TryFirstContact(target.Hurtbox,out progress,out blade);
        }
        else
        {
            radius=effect.Profile.MeleeThickness*.5f;
            var arc=new MeleeArc(origin,direction,actor.Facing,effect.Profile.Range,t.ArcDegrees*Math.PI/180,effect.Profile.MeleeThickness);
            hit=t.FirstTargetOnly ? arc.TryFirstContact(target.Hurtbox,out progress,out blade) : arc.TryContact(target.Hurtbox,out blade);
        }
        return hit && _combat.BodyContactVisible(origin,blade,radius,target.Hurtbox);
    }

    private bool TechniqueHit(AbilityEffect effect, Combatant target, uint tick, double focus)
    {
        var t = effect.Definition.Melee!;
        var basePower = effect.HitSnapshot?.Power ?? _combat.SwordPower(effect.ActorId);
        var level = (effect.ChannelSkillPower>0?effect.ChannelSkillPower:PowerFactor(_actors[effect.ActorId], effect.Profile.Id))*effect.ChargePower;
        var power = CombatBalanceMath.TechniquePower(basePower, t, target.Health, target.Stats.MaxHealth, level, focus);
        if(effect.HitSnapshot is {} snapshot)effect.HitSnapshot=snapshot with {Power=power};
        var rules=effect.Definition.HitRules;
        if(_combat.CaptureAbilityContact(effect,target,power,false,tick,true,ContactSkillPower(effect)))return false;
        var hit = _combat.ResolveDirectHit(effect.ActorId, target.Id, effect.Direction, power, magic: false,
            blockable: rules.Blockable, parryable: rules.Parryable,pressureScale: rules.PressureScale,skill:effect.Profile.Id,
            weaponExecution:effect.WeaponExecution,execution:effect.Id,snapshot:effect.HitSnapshot,context:effect.Definition.HitContext,combo:effect.Combo);
        return CompleteContact(effect,target,tick,hit,true);
    }

}
