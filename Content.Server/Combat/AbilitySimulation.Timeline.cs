using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly Dictionary<NetworkEntityId,(double Mana,double Stamina)> _continuousCosts=new();
    internal (double Mana,double Stamina) ContinuousCosts(NetworkEntityId id)=>_continuousCosts.GetValueOrDefault(id);

    internal void PrepareResourceFlows()
    {
        foreach(var id in _continuousCosts.Keys)
            if(_combat.TryGet(id,out var actor))actor.Resources.ManaFlowCredit=0;
        _continuousCosts.Clear();
        foreach(var effect in _effects)
        {
            if(effect.Phase is not (AbilityPhase.Channeling or AbilityPhase.Sustaining) || effect.Action?.Finished!=false)continue;
            var definition=effect.Channel!;var previous=ContinuousCosts(effect.ActorId);
            _continuousCosts[effect.ActorId]=(Content.Server.Stats.StatMath.Add(previous.Mana,definition.ManaPerSecond),
                Content.Server.Stats.StatMath.Add(previous.Stamina,definition.StaminaPerSecond));
        }
        foreach(var id in _continuousCosts.Keys)_combat.RefreshManaRecovery(id);
    }

    internal double NextBoundary(double end)
    {
        var next=end;
        foreach(var recovery in _actionRecoveries.Values)
            if(recovery.Action is {Finished:false} action)next=CombatSimulation.EarlierBoundary(_time,next,action.PhaseEndsAt);
        foreach(var effect in _effects)
        {
            if(effect.Phase!=AbilityPhase.Flying && effect.Action is {Finished:true,StopReason:not ActionStopReason.Completed})continue;
            if(effect.Action is {Phase:Content.Shared.Combat.CombatActionPhase.Recovery} action)
                next=CombatSimulation.EarlierBoundary(_time,next,action.PhaseEndsAt);
            var at=effect.Phase switch
            {
                AbilityPhase.Telegraph=>effect.Action!.PhaseEndsAt,
                AbilityPhase.Charging when effect.Profile.ChargeAutoRelease=>effect.ChargeStartedAt+effect.ChargeSeconds,
                AbilityPhase.Channeling or AbilityPhase.Sustaining=>effect.Channel!.NextEventAt,
                AbilityPhase.Recovery=>effect.RecoveryEndsAt,
                AbilityPhase.Dash or AbilityPhase.Impact=>effect.VisualEndsAt,
                _=>double.PositiveInfinity
            };
            next=CombatSimulation.EarlierBoundary(_time,next,at);
        }
        foreach(var (id,cost) in _continuousCosts)
            next=_combat.ChannelResourceBoundary(id,next,cost.Mana,cost.Stamina);
        foreach(var id in _cooldownAwake)
            foreach(var cooldown in _actors[id].Cooldowns)
                if(cooldown.Recovering)next=CombatSimulation.EarlierBoundary(_time,next,cooldown.NextRechargeAt);
        return PlanProjectiles(next);
    }
}
