using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Shared.Movement;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly Dictionary<NetworkEntityId,ActionCancelCommand> _cancelPending=new();
    internal void ApplyQueuedCancellations(uint tick)=>ApplyCancellations(tick);

    public bool QueueCancel(NetworkEntityId id,ActionCancelCommand command,uint tick)
    {
        if(!_actors.TryGetValue(id,out var actor) || command.Sequence==0 || command.ActionSequence==0 ||
            MovementSimulation.IsSequenceNewer(command.ObservedServerTick,tick)||
            !MovementSimulation.IsSequenceNewer(command.Sequence,actor.LastCancelSequence)) return false;
        actor.LastCancelSequence=command.Sequence;
        if(!_combat.DeliveringReceivedInput && actor.LastCancelTick==tick) return false;
        actor.LastCancelTick=tick; _cancelPending[id]=command; return true;
    }

    private void ApplyCancellations(uint tick)
    {
        foreach(var (id,command) in _cancelPending)
        {
            var cancelled=false;
            if(command.ExecutionId==0)cancelled=Inputs.CancelSkill(id,command.ActionSequence);
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
        var noHealth=!_combat.CanPayHealth(effect.ActorId,profile.HealthCost,profile.AllowFatalHealthCost);
        if(noMana || noStamina || noHealth)
        {
            var outcome=noMana ? AbilityOutcome.NoMana : noStamina?AbilityOutcome.NoStamina:AbilityOutcome.NoHealth;
            if(action.Stop(ActionStopReason.ResourceFailure)) SettleStoppedAction(effect);
            _results[effect.ActorId]=new(effect.Sequence,tick,outcome);
            ReleaseAction(effect); return false;
        }
        if(!action.Activate(_time)) return false;
        if(profile.Form==AbilityForm.Dash)
        {_combat.SpendDodge(effect.ActorId,profile.StaminaCost);_combat.StartDodgeProtection(effect.ActorId,action,effect.Definition.Dodge!);}
        else _combat.SpendStamina(effect.ActorId,profile.StaminaCost);
        actor.Mana=Math.Max(0,actor.Mana-profile.ManaCost);
        if(profile.HealthCost>0)
        {
            if(profile.AllowFatalHealthCost && _combat.Get(effect.ActorId).Health==profile.HealthCost)effect.FatalHealthPrice=profile.HealthCost;
            else _combat.PayHealth(effect.ActorId,profile.HealthCost,profile.AllowFatalHealthCost);
        }
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
            FinishEffectWeaponUse(effect);ReleaseAction(effect);
            _results[effect.ActorId]=new(effect.Sequence,tick,AbilityOutcome.InvalidState);return false;
        }
        if(effect.Definition.Channel is {} channel)
        {
            effect.Channel=new(channel.ForExecution(_calculator,_combat.Get(effect.ActorId).Stats),_time);
            if(channel.DamageCalculation==Content.Server.Data.EffectCalculationMode.Snapshot)
            {
                effect.HitSnapshot=_combat.CaptureDirectHit(effect.ActorId,effect.Profile.Form==AbilityForm.Melee?_combat.SwordPower(effect.ActorId):SpellPower(effect));
                effect.ChannelSkillPower=PowerFactor(actor,effect.Profile.Id);
            }
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
        _combat.PayPartialHealth(effect.ActorId,effect.Profile.HealthCost*fraction,effect.Profile.AllowFatalHealthCost);
        RefreshManaRecovery(effect.ActorId); _dirty.Add(effect.ActorId);
    }

    internal void InterruptCompanionActions(NetworkEntityId id,Func<ushort,bool> refuses)
    {
        if(!_combat.TryGet(id,out var actor)||actor.Kind!=CombatEntityKind.Echo)return;
        foreach(var effect in _effects)
            if(refuses(effect.Profile.Id)&&MatchesAction(effect,id,ActionStopReason.EnemyInterrupt,false,0,0,null))
                StopEffectAction(effect,ActionStopReason.EnemyInterrupt,ActionCancelPermission.None);
        if(_actionRecoveries.TryGetValue(id,out var recovery)&&refuses(recovery.Profile.Id)&&
            MatchesAction(recovery,id,ActionStopReason.EnemyInterrupt,false,0,0,null))
            StopEffectAction(recovery,ActionStopReason.EnemyInterrupt,ActionCancelPermission.None);
    }

    private bool StopActions(NetworkEntityId id,ActionStopReason reason,bool recoveryOnly=false,uint relatedSequence=0,
        ActionCancelPermission permission=ActionCancelPermission.Voluntary,ActionCapability? capabilities=null,ulong executionId=0,bool includeBasic=true)
    {
        var stopped=includeBasic && !recoveryOnly && _combat.StopBasic(id,reason,permission,relatedSequence,executionId,capabilities);
        if(includeBasic && !recoveryOnly)stopped|=_combat.StopSummon(id,reason,permission,relatedSequence,executionId,capabilities);
        // Active payloads and their ordinary recoveries have separate lifetimes.
        foreach(var effect in _effects)
        {
            if(MatchesAction(effect,id,reason,recoveryOnly,relatedSequence,executionId,capabilities))stopped|=StopEffectAction(effect,reason,permission);
        }
        if(_actionRecoveries.TryGetValue(id,out var recovery) && MatchesAction(recovery,id,reason,recoveryOnly,relatedSequence,executionId,capabilities))
            stopped|=StopEffectAction(recovery,reason,permission);
        return stopped;
    }

    private bool MatchesAction(AbilityEffect effect,NetworkEntityId id,ActionStopReason reason,bool recoveryOnly,
        uint relatedSequence,ulong executionId,ActionCapability? capabilities)
    {
        if(effect.ActorId!=id || effect.Action is not {Finished:false} ||
            recoveryOnly && effect.Profile.Form!=AbilityForm.Recovery || relatedSequence!=0 && effect.Sequence!=relatedSequence ||
            executionId!=0 && effect.Id!=executionId || capabilities is {} required && (effect.Definition.Action.Capabilities&required)==0)return false;
        return !IsBackgroundToggle(effect) || !(reason is ActionStopReason.Voluntary or ActionStopReason.Followup ||
            reason==ActionStopReason.EnemyInterrupt && (effect.Definition.Toggle!.StopOn&ToggleStopTriggers.EnemyInterrupt)==0);
    }

    private bool StopEffectAction(AbilityEffect effect,ActionStopReason reason,ActionCancelPermission permission)
    {
        var action=effect.Action!;if(!action.Stop(reason,permission))return false;
        var id=effect.ActorId;
        if(_combat.TryGet(id,out var caster) && ReferenceEquals(caster.Action,action))_combat.SetRecovery(id,0);
        SettleStoppedAction(effect);ReleaseAction(effect);_dirty.Add(id);FinishChannelCooldown(effect);ForgetToggle(effect);
        if(_actionRecoveries.TryGetValue(id,out var recovery) && ReferenceEquals(recovery.Action,action))_actionRecoveries.Remove(id);
        _combat.RecordJournal(id,id,reason==ActionStopReason.Voluntary?CombatJournalKind.ActionCancelled:CombatJournalKind.ActionInterrupted,
            effect.Profile.ManaCost*action.ResourceFractionForStop,skill:effect.Profile.Id);
        return true;
    }
}
