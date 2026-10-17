using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
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
            if(effect.Phase is AbilityPhase.Channeling or AbilityPhase.Sustaining)
                next=CombatSimulation.EarlierBoundary(_time,next,effect.Channel!.FundedUntil(_actors[effect.ActorId].Mana,
                    _combat.AvailableStamina(effect.ActorId)));
        }
        foreach(var id in _cooldownAwake)
            foreach(var cooldown in _actors[id].Cooldowns)
                if(cooldown.Recovering)next=CombatSimulation.EarlierBoundary(_time,next,cooldown.NextRechargeAt);
        return PlanProjectiles(next);
    }
}
