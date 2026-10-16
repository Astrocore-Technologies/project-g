using Content.Server.Data;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private void ApplySkillEffects(AbilityEffect execution,NetworkEntityId hitTarget=default,ResolvedHit hit=default)
    {
        foreach(var application in execution.Definition.Effects)
        {
            var active=application.Trigger==SkillEffectTrigger.Activated;
            if(active==hitTarget.IsValid) continue;
            if(!active && (hit.Evaded || hit.Guard==GuardImpact.Parried ||
                application.Trigger is SkillEffectTrigger.HealthDamage or SkillEffectTrigger.UnblockedHealthDamage && hit.Damage<=0 ||
                application.Trigger==SkillEffectTrigger.UnblockedHealthDamage && hit.Guard==GuardImpact.Blocked)) continue;
            var target=application.Target==SkillEffectTarget.Self ? execution.ActorId : hitTarget;
            if(!_combat.TryGet(target,out var recipient) || application.RequiresBleedable && !recipient.CanBleed) continue;
            var sourceAvailable=_combat.TryGet(execution.ActorId,out var sourceActor) && sourceActor.Active && sourceActor.Health>0;
            var power=application.ScalePowerWithSkillLevel ? sourceAvailable && _actors.TryGetValue(execution.ActorId,out var author)
                ? PowerFactor(author,execution.Profile.Id) : execution.ReleasedSource?.SkillPower ?? 1 : 1;
            power*=execution.ChargePower;
            switch(application.Operation)
            {
                case SkillEffectOperation.Timed:
                    _combat.ApplyEffect(execution.ActorId,target,application.EffectId!,power,execution.Id,application.AllowOtherPlayers,execution.ReleasedSource);break;
                case SkillEffectOperation.Heal:
                    _combat.ResolveHealing(execution.ActorId,target,application.Power!,power,execution.Id,application.AllowOtherPlayers);break;
                case SkillEffectOperation.Resurrect:
                    _combat.ResolveResurrection(execution.ActorId,target,application.Power!,power,execution.Id,application.AllowOtherPlayers);break;
                case SkillEffectOperation.Control:
                    ApplySkillControl(execution,target,application.Control!);break;
                case SkillEffectOperation.Cleanse:
                case SkillEffectOperation.Dispel:
                    _combat.ResolveRemoval(execution.ActorId,target,application.Removal!,application.Operation==SkillEffectOperation.Cleanse,
                        execution.Profile.Id,application.AllowOtherPlayers);break;
            }
        }
    }

    private void ApplySkillControl(AbilityEffect execution,NetworkEntityId target,SkillControlDefinition control)
    {
        // The common contact resolver already validated hostility, geometry, evasion and defense.
        // Control APIs repeat target/state/resistance checks before spending any ACR budget.
        var source=execution.ActorId;var definition=execution.Profile.Id;
        if(!CanProjectileTarget(execution,target)) return;
        var sourcePosition=_combat.TryGet(source,out var actor) ? actor.Position : execution.ReleasedSource!.Position;
        switch(control.Kind)
        {
            case Content.Shared.Combat.CrowdControlKind.Knockup:
                _combat.Knockup(target,control.DurationSeconds!.Value,control.Height!.Value,control.DownSeconds!.Value,
                    source,definition,execution.Id,execution.ReleasedSource);break;
            case Content.Shared.Combat.CrowdControlKind.Knockback:
            case Content.Shared.Combat.CrowdControlKind.Pull:
                var origin=control.Origin==ControlOrigin.Source ? sourcePosition : execution.Position;
                if(control.Kind==Content.Shared.Combat.CrowdControlKind.Pull)
                    _combat.Pull(target,origin,control.Distance!.Value,control.Speed!.Value,source,definition,execution.Id,execution.ReleasedSource);
                else
                {
                    var offset=_combat.Get(target).Position-origin;
                    var direction=offset.LengthSquared()>.000001f ? System.Numerics.Vector2.Normalize(offset) : execution.Direction;
                    _combat.Knockback(target,direction,control.Distance!.Value,control.Speed!.Value,source,definition,execution.Id,execution.ReleasedSource);
                }
                break;
            default:
                _combat.ApplyStatusControl(source,target,definition,execution.Id,control.Kind,control.DurationSeconds!.Value,
                    control.Strength ?? 1,execution.ReleasedSource);break;
        }
    }

    private bool SupportCandidate(AbilityEffect execution,NetworkEntityId target)
    {
        if(_combat.CanSupport(execution.ActorId,target))return true;
        foreach(var application in execution.Definition.Effects)
            if(application.Target==SkillEffectTarget.HitTarget && application.AllowOtherPlayers &&
                _combat.CanSupport(execution.ActorId,target,true))return true;
        return false;
    }

    private void ImportPeriodicHits(uint tick)
    {
        foreach(var effect in _combat.EffectEvents)
        {
            if(effect.Kind!=CombatEffectEventKind.PeriodicDamage || effect.Amount<=0) continue;
            var source=_combat.ResolveSource(effect.Source);
            if(source.IsValid && _combat.TryGet(effect.Target,out var target))
                _hits.Add(new(effect.Execution,source,effect.Target,tick,effect.Amount,target.Health,Critical:effect.Critical));
        }
    }
}
