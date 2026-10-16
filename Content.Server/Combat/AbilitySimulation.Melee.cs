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
        if (profile.Form == AbilityForm.Dash)
        {
            profile = profile with { StaminaCost = _combat.DodgeCostFor(id) };
            if (_combat.IsSwordsman(id)) profile = profile with { Range = _catalog.Swordsman!.DashRange, CooldownSeconds = _catalog.Swordsman.DashCooldown };
        }
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
    internal bool CancelAction(NetworkEntityId id,uint relatedSequence,ulong executionId=0) => StopActions(id,ActionStopReason.Voluntary,relatedSequence:relatedSequence,executionId:executionId);

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
        _spatial.Query(actor.Position, effect.Profile.Range + effect.Profile.Radius, _candidates);
        Combatant? first = null; var distance = float.MaxValue;
        foreach (var id in _candidates)
        {
            if (!MeleeCandidate(effect, actor, id, out var target)) continue;
            var next = Vector3.DistanceSquared(actor.Foot, target.Foot);
            if (next < distance || next == distance && (first is null || id.Value < first.Id.Value)) { first = target; distance = next; }
        }
        if (first is null) return false;
        var hit = TechniqueHit(effect, first, tick, effect.FocusFactor);
        if (technique.FirstTargetOnly) return hit;
        foreach (var id in _candidates)
            if (id != first.Id && MeleeCandidate(effect, actor, id, out var target)) hit |= TechniqueHit(effect, target, tick, 1);
        return hit;
    }

    private bool MeleeCandidate(AbilityEffect effect, Combatant actor, NetworkEntityId id, out Combatant target)
    {
        if (!_combat.TryGet(id, out target!) || !_combat.CanTarget(actor.Id, id) || Vector3.DistanceSquared(actor.Foot, target.Foot) > effect.Profile.Range * effect.Profile.Range || !_grid.ClearAttack(actor.Foot, target.Foot)) return false;
        var t = effect.Definition.Melee!;
        if (t.NarrowThrust)
        {
            var offset = target.Position - actor.Position;
            var along = Vector2.Dot(offset, effect.Direction);
            var across = MathF.Abs(offset.X * effect.Direction.Y - offset.Y * effect.Direction.X);
            return along >= 0 && along <= effect.Profile.Range && across <= effect.Profile.Radius + _grid.AgentRadius;
        }
        return BasicAttackShape.Contains(actor.Position, effect.Direction, target.Position, effect.Profile.Range, t.ArcDegrees * MathF.PI / 360);
    }

    private bool TechniqueHit(AbilityEffect effect, Combatant target, uint tick, double focus)
    {
        var t = effect.Definition.Melee!;
        var basePower = _combat.SwordPower(effect.ActorId);
        var level = PowerFactor(_actors[effect.ActorId], effect.Profile.Id)*effect.ChargePower;
        var power = CombatBalanceMath.TechniquePower(basePower, t, target.Health, target.Stats.MaxHealth, level, focus);
        var rules=effect.Definition.HitRules;
        var hit = _combat.ResolveDirectHit(effect.ActorId, target.Id, effect.Direction, power, magic: false,
            blockable: rules.Blockable, parryable: rules.Parryable,pressureScale: rules.PressureScale,skill:effect.Profile.Id,
            weaponExecution:effect.WeaponExecution);
        var damage = hit.Damage; var guard = hit.Guard;
        ApplySkillEffects(effect,target.Id,hit);
        _hits.Add(new(effect.Id, effect.ActorId, target.Id, tick, damage, target.Health, guard, hit.Critical, hit.Evaded));
        _combat.SwordHit(effect.ActorId, basic: false, success: damage > 0);
        if (damage <= 0) return false;
        if (target.Kind != CombatEntityKind.Player && _practiced.Add(effect.Id)) _practice.Add((effect.ActorId, effect.Profile.Id));
        return guard == GuardImpact.None;
    }

}
