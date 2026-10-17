using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly HashSet<NetworkEntityId> _cooldownAwake=new();
    private readonly List<NetworkEntityId> _cooldownSleeping=new();

    private AbilityProfile CooldownProfile(NetworkEntityId id,AbilityActor actor,int index)
    {
        var state=actor.Cooldowns[index].State(_time);
        return ExecutionProfile(id,actor,index) with {ReadyInSeconds=state.ReadyIn,Charges=state.Available,MaxCharges=state.Maximum,PendingEndCharges=state.PendingEnd,
            RechargeInSeconds=state.RechargeIn,RechargeDuration=state.RechargeDuration,ToggleState=ToggleStateFor(id,actor.Profiles[index].Id)};
    }

    private void ConfigureCooldowns(NetworkEntityId id,AbilityActor actor)
    {
        for(var i=0;i<actor.Cooldowns.Length;i++) actor.Cooldowns[i].Configure(ExecutionProfile(id,actor,i).CooldownSeconds,_time);
        WakeCooldowns(id,actor);
    }

    private void ConsumeCooldown(AbilityEffect effect,bool startNow=false)
    {
        var actor=_actors[effect.ActorId];var index=CurrentAbilityIndex(actor,effect.Profile.Id);var slot=actor.Cooldowns[index];
        var recovering=slot.Recovering;
        slot.Configure(ExecutionProfile(effect.ActorId,actor,index).CooldownSeconds,_time);
        var deferred=!startNow && effect.Definition.Channel?.CooldownStart==Content.Server.Data.ChannelCooldownStart.Completion;
        if(!(deferred?slot.ReserveUntilEnd(_time):slot.Consume(_time))) throw new InvalidOperationException("An exclusive action lost its validated charge before activation.");
        if(!recovering&&slot.Recovering)_combat.RecordJournal(effect.ActorId,effect.ActorId,Content.Shared.Combat.CombatJournalKind.CooldownStarted,
            duration:slot.State(_time).RechargeIn,skill:effect.Profile.Id);
        _dirty.Add(effect.ActorId);
        WakeCooldowns(effect.ActorId,actor);
    }

    private void FinishChannelCooldown(AbilityEffect effect,double? endedAt=null)
    {
        if(effect.Definition.Channel?.CooldownStart!=Content.Server.Data.ChannelCooldownStart.Completion ||
            effect.Action?.Activated!=true || !_actors.TryGetValue(effect.ActorId,out var actor))return;
        var index=CurrentAbilityIndex(actor,effect.Profile.Id);var slot=actor.Cooldowns[index];var now=endedAt ?? _time;
        slot.Configure(ExecutionProfile(effect.ActorId,actor,index).CooldownSeconds,now);
        var recovering=slot.Recovering;
        if(slot.FinishReservation(now))
        {
            if(!recovering&&slot.Recovering)_combat.RecordJournal(effect.ActorId,effect.ActorId,Content.Shared.Combat.CombatJournalKind.CooldownStarted,
                duration:slot.State(now).RechargeIn,skill:effect.Profile.Id);
            _dirty.Add(effect.ActorId);WakeCooldowns(effect.ActorId,actor);
        }
    }

    private static int CurrentAbilityIndex(AbilityActor actor,ushort skill)
    {
        for(var i=0;i<actor.Profiles.Length;i++)if(actor.Profiles[i].Id==skill)return i;
        throw new InvalidOperationException("An executing skill is absent from its owner's loadout.");
    }

    internal bool ResetCooldown(NetworkEntityId id,ushort skill)
    {
        if(!_actors.TryGetValue(id,out var actor)) return false;
        for(var i=0;i<actor.Profiles.Length;i++)
        {
            if(actor.Profiles[i].Id!=skill) continue;
            actor.Cooldowns[i].Reset(_time); _dirty.Add(id); WakeCooldowns(id,actor); return true;
        }
        return false;
    }

    private void WakeCooldowns(NetworkEntityId id,AbilityActor actor)
    {
        foreach(var slot in actor.Cooldowns)
            if(slot.Recovering) { _cooldownAwake.Add(id); return; }
        _cooldownAwake.Remove(id);
    }

    private void AdvanceCooldowns()
    {
        _cooldownSleeping.Clear();
        foreach(var id in _cooldownAwake)
        {
            if(!_actors.TryGetValue(id,out var actor)) { _cooldownSleeping.Add(id); continue; }
            var recovering=false;
            foreach(var slot in actor.Cooldowns)
            {
                if(slot.Advance(_time)) _dirty.Add(id);
                recovering|=slot.Recovering;
            }
            if(!recovering) _cooldownSleeping.Add(id);
        }
        foreach(var id in _cooldownSleeping) _cooldownAwake.Remove(id);
    }
}
