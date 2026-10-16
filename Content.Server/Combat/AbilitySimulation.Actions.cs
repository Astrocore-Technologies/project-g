using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Shared.Movement;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly Dictionary<NetworkEntityId,ActionCancelCommand> _cancelPending=new();

    public bool QueueCancel(NetworkEntityId id,ActionCancelCommand command,uint tick)
    {
        if(!_actors.TryGetValue(id,out var actor) || command.Sequence==0 || command.ActionSequence==0 ||
            !MovementSimulation.IsSequenceNewer(command.Sequence,actor.LastCancelSequence)) return false;
        actor.LastCancelSequence=command.Sequence;
        if(actor.LastCancelTick==tick) return false;
        actor.LastCancelTick=tick; _cancelPending[id]=command; return true;
    }

    private void ApplyCancellations(uint tick)
    {
        foreach(var (id,command) in _cancelPending)
        {
            var cancelled=false;
            if(command.ExecutionId==0 && _pending.TryGetValue(id,out var pending) && pending.Command.Sequence==command.ActionSequence)
            { _pending.Remove(id); cancelled=true; }
            if(command.ExecutionId==0 && _buffered.TryGetValue(id,out var buffered) && buffered.Intent.Command.Sequence==command.ActionSequence)
            { _buffered.Remove(id); cancelled=true; }
            cancelled|=CancelAction(id,command.ActionSequence,command.ExecutionId);
            _results[id]=new(command.ActionSequence,tick,cancelled ? AbilityOutcome.Cancelled : AbilityOutcome.CancelDenied);
        }
        _cancelPending.Clear();
    }

    private bool ActivateAction(ref AbilityEffect effect,uint tick)
    {
        var action=effect.Action!;
        if(!action.AwaitingActivation(_time)) return action.Activated;
        var actor=_actors[effect.ActorId]; var profile=effect.Profile;
        var noMana=actor.Mana<profile.ManaCost || effect.Definition.Channel is {ManaPerSecond:>0} && actor.Mana<=profile.ManaCost;
        var noStamina=!_combat.CanSpendStamina(effect.ActorId,profile.StaminaCost) ||
            effect.Definition.Channel is {StaminaPerSecond:>0} && _combat.AvailableStamina(effect.ActorId)<=profile.StaminaCost;
        if(noMana || noStamina)
        {
            var outcome=noMana ? AbilityOutcome.NoMana : AbilityOutcome.NoStamina;
            if(action.Stop(ActionStopReason.ResourceFailure)) SettleStoppedAction(effect);
            _results[effect.ActorId]=new(effect.Sequence,tick,outcome);
            ReleaseAction(effect); return false;
        }
        if(!action.Activate(_time)) return false;
        if(profile.Form==AbilityForm.Dash) _combat.SpendDodge(effect.ActorId);
        else _combat.SpendStamina(effect.ActorId,profile.StaminaCost);
        actor.Mana=Math.Max(0,actor.Mana-profile.ManaCost);
        ConsumeCooldown(effect);
        if(effect.Definition.Melee?.RequiresParry==true) _combat.ConsumeRiposte(effect.ActorId);
        if(profile.Form==AbilityForm.Melee) effect.FocusFactor=_combat.ConsumeFocus(effect.ActorId);
        RefreshManaRecovery(effect.ActorId); _dirty.Add(effect.ActorId);
        _combat.RecordJournal(effect.ActorId,effect.ActorId,CombatJournalKind.ActionActivated,profile.ManaCost,skill:profile.Id);
        if((effect.Definition.Action.Capabilities&ActionCapability.Weapon)!=0)
        {effect.WeaponExecution=effect.Id;_combat.BeginWeaponUse(effect.ActorId,effect.Id);}
        ApplySkillEffects(effect);
        if(_combat.Get(effect.ActorId).Health<=0)
        {
            action.Stop(ActionStopReason.Death);FinishChannelCooldown(effect);ForgetToggle(effect);
            _combat.EndWeaponUse(effect.ActorId,effect.Id);ReleaseAction(effect);
            _results[effect.ActorId]=new(effect.Sequence,tick,AbilityOutcome.InvalidState);return false;
        }
        if(effect.Definition.Channel is {} channel)
        {
            effect.Channel=new(channel.ForExecution(_calculator,_combat.Get(effect.ActorId).Stats),_time);
            if(channel.DamageCalculation==Content.Server.Data.EffectCalculationMode.Snapshot)
                effect.HitSnapshot=_combat.CaptureDirectHit(effect.ActorId,SpellPower(effect));
        }
        if(IsBackgroundToggle(effect) && ReferenceEquals(_combat.Get(effect.ActorId).Action,action))
            _combat.Get(effect.ActorId).Action=null;
        return true;
    }

    private void CompleteStartupAction(AbilityEffect effect)
    {
        effect.Action?.BeginRecovery(_time);
        ReleaseAction(effect);
    }

    private void ReleaseAction(AbilityEffect effect)
    {
        if(effect.Action?.Finished==true && _combat.TryGet(effect.ActorId,out var caster) && ReferenceEquals(caster.Action,effect.Action))
        {caster.Action=null;_combat.SettleWeaponBoundary(effect.ActorId);}
    }

    private void SettleStoppedAction(AbilityEffect effect)
    {
        var action=effect.Action!;var fraction=action.ResourceFractionForStop;
        if(!_actors.TryGetValue(effect.ActorId,out var actor)) return;
        if(!action.Activated && action.StopReason is ActionStopReason.EnemyInterrupt or ActionStopReason.ResourceFailure or ActionStopReason.Death)
            ConsumeCooldown(effect,startNow:true);
        if(fraction<=0)return;
        actor.Mana=Math.Max(0,actor.Mana-effect.Profile.ManaCost*fraction);
        _combat.SpendStamina(effect.ActorId,effect.Profile.StaminaCost*fraction);
        RefreshManaRecovery(effect.ActorId); _dirty.Add(effect.ActorId);
    }

    private bool StopActions(NetworkEntityId id,ActionStopReason reason,bool recoveryOnly=false,uint relatedSequence=0,
        ActionCancelPermission permission=ActionCancelPermission.Voluntary,ActionCapability? capabilities=null,ulong executionId=0)
    {
        var stopped=false;
        // Visits bounded active effects, never the idle world. An already launched projectile has a finished action.
        foreach(var effect in _effects)
        {
            if(effect.ActorId!=id || effect.Action is not {} action || action.Finished ||
                recoveryOnly && effect.Profile.Form!=AbilityForm.Recovery || relatedSequence!=0 && effect.Sequence!=relatedSequence ||
                executionId!=0 && effect.Id!=executionId) continue;
            if(capabilities is {} required && (effect.Definition.Action.Capabilities&required)==0)continue;
            if(IsBackgroundToggle(effect) && (reason is ActionStopReason.Voluntary or ActionStopReason.Followup ||
                reason==ActionStopReason.EnemyInterrupt && (effect.Definition.Toggle!.StopOn&ToggleStopTriggers.EnemyInterrupt)==0))continue;
            if(!action.Stop(reason,permission)) continue;
            if(_combat.TryGet(id,out var caster) && ReferenceEquals(caster.Action,action)) _combat.SetRecovery(id,0);
            SettleStoppedAction(effect); ReleaseAction(effect); _dirty.Add(id); stopped=true;
            FinishChannelCooldown(effect);
            ForgetToggle(effect);
            _combat.RecordJournal(id,id,reason==ActionStopReason.Voluntary?CombatJournalKind.ActionCancelled:CombatJournalKind.ActionInterrupted,
                effect.Profile.ManaCost*action.ResourceFractionForStop,skill:effect.Profile.Id);
        }
        return stopped;
    }
}
