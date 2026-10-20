using System.Numerics;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private void AdvanceChannel(ref AbilityEffect effect,uint tick)
    {
        var caster=_combat.Get(effect.ActorId);
        var channel=effect.Channel!;
        while(true)
        {
            // A pulse may change passive/effect stats and replace the resource profile.
            // Continue payment against that current profile, never a detached actor copy.
            var actor=_actors[effect.ActorId];
            // Pay each pulse boundary before resolving it. Reflection/control may end the channel there;
            // the rest of this server step must never be charged in advance.
            var horizon=Math.Min(_time,channel.NextEventAt);
            var resources=caster.Resources;
            var step=channel.Advance(horizon,Content.Server.Stats.StatMath.Add(actor.Mana,resources.ManaFlowCredit),_combat.AvailableStamina(effect.ActorId));
            var credit=Math.Min(resources.ManaFlowCredit,step.Mana);resources.ManaFlowCredit-=credit;
            actor.Mana=Math.Max(0,actor.Mana-(step.Mana-credit));if(step.Stamina>0)_combat.SpendStamina(effect.ActorId,step.Stamina);
            if(step.Mana>0)
            {
                _combat.RecordJournal(effect.ActorId,effect.ActorId,Content.Shared.Combat.CombatJournalKind.ManaSpent,step.Mana,skill:effect.Profile.Id);
                _dirty.Add(effect.ActorId);RefreshManaRecovery(effect.ActorId);
            }
            effect.Remaining=(float)(channel.Remaining ?? 0);
            for(var i=0;i<step.Pulses;i++)
            {
                if(caster.Health<=0 || effect.Action!.Finished)break;
                if(effect.Profile.Form==AbilityForm.GroundArea && (Vector3.DistanceSquared(caster.Foot,effect.Foot)>effect.Profile.Range*effect.Profile.Range || !_grid.ClearAttack(caster.Foot,effect.Foot)))continue;
                var pulse=effect;pulse.Id=_combat.AllocateExecution();pulse.Action=null;pulse.Channel=null;
                if(effect.Profile.Form==AbilityForm.Projectile)
                {
                    if(_effects.Count>=_options.MaxAbilityEffects)
                    {
                        _results[effect.ActorId]=new(effect.Sequence,tick,AbilityOutcome.Capacity);
                        StopEffectAction(effect,ActionStopReason.Policy,Content.Shared.Combat.ActionCancelPermission.None);
                        effect.Phase=AbilityPhase.Finished;return;
                    }
                    pulse.Origin=pulse.Position=caster.Position;pulse.OriginHeight=pulse.Height=caster.Foot.Y+pulse.Profile.ProjectileLaunchHeight;
                    pulse.Phase=AbilityPhase.Flying;
                    pulse.HitSnapshot??=_combat.CaptureDirectHit(effect.ActorId,SpellPower(effect));
                    pulse.ReleasedSource=_combat.CaptureReleasedSource(effect.ActorId,ContactSkillPower(effect));
                    RetainChannelProjectile(pulse);
                    ReleaseProjectile(ref pulse,tick);
                    if(pulse.Phase==AbilityPhase.Finished)FinishEffectWeaponUse(pulse);else _effects.Add(pulse);
                }
                else if(effect.Profile.Form is AbilityForm.Recovery or AbilityForm.Melee)ResolveTechnique(pulse,tick);
                else HitArea(pulse,tick);
                if(IsBackgroundToggle(effect) && (effect.Definition.Action.Capabilities&Content.Shared.Combat.ActionCapability.Weapon)!=0)
                    _combat.SettleWeaponPulseBoundary(effect.ActorId);
            }
            if(caster.Health<=0 || effect.Action!.Finished){effect.Phase=AbilityPhase.Finished;return;}
            if(step.Finished)
            {
                if(step.Starved)_results[effect.ActorId]=new(effect.Sequence,tick,step.ManaLimited
                    ? AbilityOutcome.NoMana:AbilityOutcome.NoStamina);
                break;
            }
            if(horizon>=_time)return;
        }
        FinishChannelCooldown(effect,channel.PaidUntil);
        ForgetToggle(effect);
        if(IsBackgroundToggle(effect))
        {effect.Action!.BeginRecovery(channel.PaidUntil,0);effect.Phase=AbilityPhase.Finished;effect.Remaining=0;return;}
        effect.Action!.BeginRecovery(channel.PaidUntil);effect.Action.Advance(_time);
        _combat.SetRecovery(effect.ActorId,Math.Max(0,effect.Action.PhaseEndsAt-_time));
        effect.RecoveryEndsAt=caster.RecoveryUntil;
        effect.Phase=effect.Action.Finished?AbilityPhase.Finished:AbilityPhase.Recovery;
        effect.Remaining=(float)Math.Max(0,effect.Action.PhaseEndsAt-_time);ReleaseAction(effect);
    }
}
