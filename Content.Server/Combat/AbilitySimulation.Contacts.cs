using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly record struct TechniqueContact(AbilityEffect Effect,bool Hit);
    private readonly Dictionary<ulong,TechniqueContact> _contactTechniques=new();

    private double ContactSkillPower(AbilityEffect effect)=>effect.ReleasedSource?.SkillPower ??
        (_actors.TryGetValue(effect.ActorId,out var actor)?PowerFactor(actor,effect.Profile.Id):1);

    internal bool CompleteContact(AbilityEffect effect,Combatant target,uint tick,ResolvedHit hit,bool technique)
    {
        ApplySkillEffects(effect,target.Id,hit);
        var published=new AbilityHit(effect.Id,effect.ActorId,target.Id,tick,hit.Damage,target.Health,hit.Guard,hit.Critical,hit.Evaded,hit.Tags);
        _hits.Add(published);_directHits.Add(published);
        if(technique)_combat.SwordHit(effect.ActorId,basic:false,success:hit.Damage>0);
        if(hit.Damage>0 && !CombatSimulation.IsPlayerUnit(target) && _practiced.Add(effect.Id))_practice.Add((effect.ActorId,effect.Profile.Id));
        var success=hit.Damage>0 && hit.Guard==GuardImpact.None;
        if(success && _contactTechniques.TryGetValue(effect.Id,out var contact))_contactTechniques[effect.Id]=contact with {Hit=true};
        return success;
    }

    private void SetTechniqueRecovery(ref AbilityEffect effect,bool hit)
    {
        if(!_combat.TryGet(effect.ActorId,out var caster) || caster.Health<=0 || effect.Action is {Finished:true})return;
        var technique=effect.Definition.Melee!;var recovery=hit?technique.HitRecoverySeconds:technique.MissRecoverySeconds;
        _combat.SetRecovery(effect.ActorId,recovery,hit?technique.HitCancelAfterSeconds:0,hit?technique.HitFollowups:null);
        effect.RecoveryEndsAt=caster.RecoveryUntil;effect.Action?.BeginRecovery(_time,recovery);
        TrackRecovery(effect);
    }

    internal void FinishContactTechniques()
    {
        foreach(var (execution,contact) in _contactTechniques)
        {
            var effect=contact.Effect;SetTechniqueRecovery(ref effect,contact.Hit);
            for(var i=0;i<_effects.Count;i++)if(_effects[i].Id==execution)
            {var stored=_effects[i];stored.RecoveryEndsAt=effect.RecoveryEndsAt;_effects[i]=stored;break;}
        }
        _contactTechniques.Clear();
    }
}
