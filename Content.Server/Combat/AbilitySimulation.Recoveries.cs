using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly Dictionary<NetworkEntityId,AbilityEffect> _actionRecoveries=new();
    private readonly List<NetworkEntityId> _recoveryActors=new();

    private void TrackRecovery(AbilityEffect effect)
    {
        if(effect.Action is {Phase:CombatActionPhase.Recovery} action && _combat.TryGet(effect.ActorId,out var caster) &&
            ReferenceEquals(caster.Action,action))_actionRecoveries[effect.ActorId]=effect;
    }

    private void AdvanceRecoveries()
    {
        _recoveryActors.Clear();_recoveryActors.AddRange(_actionRecoveries.Keys);
        foreach(var id in _recoveryActors)
        {
            if(!_actionRecoveries.TryGetValue(id,out var effect))continue;
            var action=effect.Action!;
            if(!_combat.TryGet(id,out var caster) || !ReferenceEquals(caster.Action,action))
            {_actionRecoveries.Remove(id);continue;}
            action.Advance(_time);
            if(!action.Finished)continue;
            caster.Action=null;_actionRecoveries.Remove(id);_combat.SettleWeaponBoundary(id);
        }
    }

    private void RemoveFinishedEffects()
    {
        for(var i=_effects.Count-1;i>=0;i--)
        {
            var effect=_effects[i];
            if(effect.Phase!=AbilityPhase.Finished &&
                (effect.Phase==AbilityPhase.Flying && effect.ReleasedSource is not null ||
                 effect.Action is not {Finished:true,StopReason:not ActionStopReason.Completed}))continue;
            ForgetToggle(effect);ReleaseAction(effect);FinishEffectWeaponUse(effect);_effects.RemoveAt(i);
        }
    }
}
