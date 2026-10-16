using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly Dictionary<NetworkEntityId,AbilityControlCommand> _chargeControls=new();
    private readonly List<NetworkEntityId> _finishedChargeControls=new();

    private void ClearChargeControls()
    {
        _finishedChargeControls.Clear();
        foreach(var (id,command) in _chargeControls)
            if(command.ExecutionId!=0 || !_buffered.TryGetValue(id,out var pending) || pending.Intent.Command.Sequence!=command.ActionSequence)_finishedChargeControls.Add(id);
        foreach(var id in _finishedChargeControls)_chargeControls.Remove(id);
    }

    public bool QueueControl(NetworkEntityId id,AbilityControlCommand command,uint tick)
    {
        if(!_actors.TryGetValue(id,out var actor) || command.Sequence==0 || command.ActionSequence==0 || !Enum.IsDefined(command.Kind) ||
            !MovementSimulation.IsSequenceNewer(command.Sequence,actor.LastControlSequence) ||
            !float.IsFinite(command.Aim.X) || !float.IsFinite(command.Aim.Y) || !float.IsFinite(command.AimHeight) || MathF.Abs(command.AimHeight)>128 ||
            !float.IsFinite(command.DirectionY) || MathF.Abs(command.DirectionY)>1)return false;
        actor.LastControlSequence=command.Sequence;
        if(actor.LastControlTick==tick && (command.Kind==AbilityControlKind.Aim ||
            _chargeControls.TryGetValue(id,out var previous) && previous.Kind==AbilityControlKind.Release))return false;
        actor.LastControlTick=tick;_chargeControls[id]=command;return true;
    }

    private bool AdvanceCharge(ref AbilityEffect effect,uint tick)
    {
        var caster=_combat.Get(effect.ActorId);var definition=effect.Definition.Charge!;
        effect.Origin=caster.Position;effect.OriginHeight=caster.Foot.Y;
        if(effect.Profile.Form!=AbilityForm.GroundArea){effect.Position=caster.Position;effect.Height=caster.Foot.Y;}
        var release=false;
        if(_chargeControls.TryGetValue(effect.ActorId,out var command) && command.ActionSequence==effect.Sequence &&
            (command.ExecutionId==0 || command.ExecutionId==effect.Id))
        {
            _chargeControls.Remove(effect.ActorId);
            if(MovementSimulation.IsSequenceNewer(command.ObservedServerTick,tick))
                _results[effect.ActorId]=new(effect.Sequence,tick,AbilityOutcome.InvalidAim);
            else if(!effect.Action!.CanTurn || UpdateChargeAim(ref effect,command))release=command.Kind==AbilityControlKind.Release;
            else _results[effect.ActorId]=new(effect.Sequence,tick,AbilityOutcome.InvalidAim);
        }
        var elapsed=Math.Max(0,_time-effect.ChargeStartedAt);
        effect.Remaining=(float)Math.Max(0,effect.ChargeSeconds-elapsed);
        if(!release && !(effect.Profile.ChargeAutoRelease && elapsed>=effect.ChargeSeconds))return false;
        effect.ChargePower=definition.Power(elapsed/effect.ChargeSeconds);
        if(!effect.Action!.ReleaseCharge(_time))return false;
        effect.CompensationSeconds=0; // Held time and client timestamps never grant projectile catch-up.
        effect.Phase=AbilityPhase.Telegraph;effect.Remaining=0;
        return true;
    }

    private bool UpdateChargeAim(ref AbilityEffect effect,AbilityControlCommand command)
    {
        var caster=_combat.Get(effect.ActorId);var profile=effect.Profile;
        if(profile.Form==AbilityForm.GroundArea)
        {
            if(command.DirectionY!=0)return false;
            var foot=new Vector3(command.Aim.X,command.AimHeight,command.Aim.Y);
            if(_grid.Surface is {} surface && surface.TryLocate(foot,.35f,out var ground))foot=ground.Position;
            if(Vector3.DistanceSquared(caster.Foot,foot)>profile.Range*profile.Range || !_grid.IsOnSurface(foot) || !_grid.ClearAttack(caster.Foot,foot))return false;
            effect.Position=new(foot.X,foot.Z);effect.Height=foot.Y;return true;
        }
        var length=command.Aim.LengthSquared()+command.DirectionY*command.DirectionY;
        if(length is <.99f or >1.01f || profile.Form!=AbilityForm.Projectile && command.DirectionY!=0)return false;
        var magnitude=MathF.Sqrt(length);effect.Direction=command.Aim/magnitude;effect.DirectionY=command.DirectionY/magnitude;return true;
    }

    private bool ApplyHoldControl(ref AbilityEffect effect,uint tick)
    {
        if(!_chargeControls.TryGetValue(effect.ActorId,out var command) || command.ActionSequence!=effect.Sequence ||
            command.ExecutionId!=0 && command.ExecutionId!=effect.Id)return true;
        _chargeControls.Remove(effect.ActorId);
        if(MovementSimulation.IsSequenceNewer(command.ObservedServerTick,tick))
        {_results[effect.ActorId]=new(effect.Sequence,tick,AbilityOutcome.InvalidAim);return true;}
        if(command.Kind==AbilityControlKind.Release)
        {
            if(!effect.Action!.Stop(ActionStopReason.Voluntary))return true;
            SettleStoppedAction(effect);FinishChannelCooldown(effect);ReleaseAction(effect);_dirty.Add(effect.ActorId);
            _combat.RecordJournal(effect.ActorId,effect.ActorId,CombatJournalKind.ActionCancelled,skill:effect.Profile.Id);
            _results[effect.ActorId]=new(effect.Sequence,tick,AbilityOutcome.Cancelled);effect.Phase=AbilityPhase.Finished;return false;
        }
        if(effect.Action!.CanTurn && !UpdateChargeAim(ref effect,command))
            _results[effect.ActorId]=new(effect.Sequence,tick,AbilityOutcome.InvalidAim);
        return true;
    }
}
