using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private sealed class Bleed
    {
        public required NetworkEntityId Source, Target;
        public required ulong Effect;
        public required double PowerPerSecond, Remaining;
    }
    private readonly Dictionary<(NetworkEntityId Source, NetworkEntityId Target), Bleed> _bleeds = new();
    private readonly List<(NetworkEntityId Source, NetworkEntityId Target)> _expiredBleeds = new();
    private readonly HashSet<ulong> _cancelledCasts = new();
    internal Action<NetworkEntityId>? StopForCast { get; set; }

    private AbilityProfile ExecutionProfile(NetworkEntityId id, AbilityActor actor, int index)
    {
        var profile = actor.Profiles[index]; var definition = actor.Definitions[index];
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
        return profile with { Area = DescribeArea(definition, profile) };
    }

    internal void CancelRecovery(NetworkEntityId id) => CancelCast(id, recoveryOnly: true);
    internal void InterruptCast(NetworkEntityId id) => CancelCast(id, recoveryOnly: false);
    private void CancelCast(NetworkEntityId id, bool recoveryOnly)
    {
        // Bound by the configured active effect budget; never visits idle actors or world entities.
        foreach (var effect in _effects)
        {
            if (effect.ActorId != id || effect.Phase is not (AbilityPhase.Telegraph or AbilityPhase.Dash) ||
                recoveryOnly && effect.Profile.Form != AbilityForm.Recovery || !recoveryOnly && !effect.Definition.Interruptible) continue;
            _cancelledCasts.Add(effect.Id);
            if (_combat.TryGet(id, out var caster)) { caster.IsCasting = false; caster.StationaryCast = false; }
            _dirty.Add(id);
        }
    }

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
        var level = PowerFactor(_actors[effect.ActorId], effect.Profile.Id);
        var power = CombatBalanceMath.TechniquePower(basePower, t, target.Health, target.Stats.MaxHealth, level, focus);
        var damage = _combat.ApplySwordDamage(effect.ActorId, target.Id, effect.Direction, power, t.ArmorIgnore, out var guard);
        _hits.Add(new(effect.Id, effect.ActorId, target.Id, tick, damage, target.Health, guard));
        _combat.SwordHit(effect.ActorId, basic: false, success: damage > 0);
        if (damage <= 0) return false;
        if (target.Kind != CombatEntityKind.Player && _practiced.Add(effect.Id)) _practice.Add((effect.ActorId, effect.Profile.Id));
        if (t.StunSeconds > 0) _combat.Stun(target.Id, t.StunSeconds);
        if (t.SlowSeconds > 0) _combat.Slow(target.Id, t.SlowFraction, t.SlowSeconds);
        if (t.BleedSeconds > 0 && target.CanBleed && !(t.BlockPreventsBleed && guard == GuardImpact.Blocked))
        {
            var key = (effect.ActorId, target.Id);
            if (_bleeds.ContainsKey(key) || _bleeds.Count < _options.MaxAbilityEffects)
                _bleeds[key] = new() { Source = effect.ActorId, Target = target.Id, Effect = effect.Id,
                    PowerPerSecond = basePower * t.BleedFactor * level / t.BleedSeconds, Remaining = t.BleedSeconds };
        }
        if (t.KnockupSeconds > 0) _combat.Knockup(target.Id,t.KnockupSeconds,t.KnockupHeight,t.KnockdownSeconds);
        if (t.KnockbackDistance > 0)
        {
            var offset = target.Position - _combat.Get(effect.ActorId).Position;
            var direction = offset.LengthSquared() > .000001f ? Vector2.Normalize(offset) : effect.Direction;
            _combat.Knockback(target.Id,direction,t.KnockbackDistance,t.KnockbackSpeed);
        }
        return guard == GuardImpact.None;
    }

    private void AdvanceBleeds(float delta, uint tick)
    {
        _expiredBleeds.Clear();
        foreach (var (key, bleed) in _bleeds)
        {
            if (!_combat.TryGet(bleed.Source, out var source) || source.Health <= 0 ||
                !_combat.TryGet(bleed.Target, out var victim) || !_combat.CanTarget(bleed.Source, bleed.Target))
            { _expiredBleeds.Add(key); continue; }
            var amount = _combat.ApplyBleedDamage(bleed.Source, bleed.Target, bleed.PowerPerSecond * Math.Min(delta, bleed.Remaining));
            bleed.Remaining -= delta;
            _hits.Add(new(bleed.Effect, bleed.Source, bleed.Target, tick, amount, victim.Health));
            if (bleed.Remaining <= .000001 || victim.Health <= 0) _expiredBleeds.Add(key);
        }
        foreach (var key in _expiredBleeds) _bleeds.Remove(key);
    }

    private void RemoveMeleeEffects(NetworkEntityId id)
    {
        _expiredBleeds.Clear();
        foreach (var (key, bleed) in _bleeds) if (bleed.Source == id || bleed.Target == id) _expiredBleeds.Add(key);
        foreach (var key in _expiredBleeds) _bleeds.Remove(key);
        foreach (var effect in _effects) if (effect.ActorId == id) _cancelledCasts.Remove(effect.Id);
    }
}
