using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private bool MissingBackgroundToggleWeapon(NetworkEntityId id,Content.Server.Data.AbilityDefinition definition)=>
        definition.Toggle is {OccupiesAction:false} && (definition.Action.Capabilities&ActionCapability.Weapon)!=0 &&
        _combat.Get(id).Weapon.Id==_catalog.UnarmedWeapon.Id;
    private readonly Dictionary<(NetworkEntityId Actor,ushort Skill),ActionInstance> _toggles=new();
    private readonly Dictionary<NetworkEntityId,int> _toggleCounts=new();
    internal bool IsToggleAbility(NetworkEntityId id,ushort skill)
    {
        if(!_actors.TryGetValue(id,out var actor) || !actor.Enabled.Contains(skill))return false;
        foreach(var definition in actor.Definitions)if(definition.NetworkId==skill)return definition.Toggle is not null;
        return false;
    }
    private void RegisterToggle(AbilityEffect effect)
    {
        if(effect.Definition.Toggle is null)return;
        _toggles.Add((effect.ActorId,effect.Profile.Id),effect.Action!);
        _toggleCounts[effect.ActorId]=_toggleCounts.GetValueOrDefault(effect.ActorId)+1;
    }
    private bool IsBackgroundToggle(AbilityEffect effect)=>effect.Definition.Toggle is {OccupiesAction:false} && effect.Action?.Activated==true;
    private ToggleState ToggleStateFor(NetworkEntityId id,ushort skill)=>_toggles.TryGetValue((id,skill),out var action) && !action.Finished
        ? action.Activated ? ToggleState.Active : ToggleState.Preparing : ToggleState.Off;
    private void ForgetToggle(AbilityEffect effect)
    {
        if(effect.Definition.Toggle is null || !_toggles.Remove((effect.ActorId,effect.Profile.Id)))return;
        var count=_toggleCounts[effect.ActorId]-1;
        if(count==0)_toggleCounts.Remove(effect.ActorId);else _toggleCounts[effect.ActorId]=count;
        _dirty.Add(effect.ActorId);
    }

    private bool SwitchToggleOff(NetworkEntityId id,ushort skill,uint sequence,uint tick)
    {
        if(!_toggles.TryGetValue((id,skill),out var action) || action.Finished)return false;
        foreach(var effect in _effects)
        {
            if(!ReferenceEquals(effect.Action,action))continue;
            var stopped=StopToggle(effect,ActionStopReason.Voluntary);
            _results[id]=new(sequence,tick,stopped?AbilityOutcome.Cancelled:AbilityOutcome.CancelDenied);return true;
        }
        return false;
    }

    private bool StopToggle(AbilityEffect effect,ActionStopReason reason)
    {
        if(effect.Action is not {} action || !action.Stop(reason))return false;
        SettleStoppedAction(effect);FinishChannelCooldown(effect);ForgetToggle(effect);
        if(_combat.TryGet(effect.ActorId,out var actor) && ReferenceEquals(actor.Action,action))_combat.SetRecovery(effect.ActorId,0);
        ReleaseAction(effect);_dirty.Add(effect.ActorId);
        _combat.RecordJournal(effect.ActorId,effect.ActorId,reason==ActionStopReason.Voluntary?CombatJournalKind.ActionCancelled:CombatJournalKind.ActionInterrupted,
            skill:effect.Profile.Id);return true;
    }

    internal void NotifySustainAction(NetworkEntityId id,ToggleStopTriggers trigger)
    {
        if(!_toggleCounts.ContainsKey(id))return;
        foreach(var effect in _effects)
            if(effect.ActorId==id && effect.Definition.Toggle is {} toggle && (toggle.StopOn&trigger)!=0 && effect.Action?.Activated==true)
                StopToggle(effect,ActionStopReason.Policy);
    }

    private void StopUnarmedWeaponToggles(NetworkEntityId id)
    {
        if(!_toggleCounts.ContainsKey(id) || _combat.Get(id).Weapon.Id!=_catalog.UnarmedWeapon.Id)return;
        foreach(var effect in _effects)
            if(effect.ActorId==id && IsBackgroundToggle(effect) &&
                (effect.Definition.Action.Capabilities&ActionCapability.Weapon)!=0 && effect.Action is {Finished:false})
                StopToggle(effect,ActionStopReason.Policy);
    }

    private void StopUnassignedToggles(NetworkEntityId id,HashSet<ushort> enabled)
    {
        if(!_toggleCounts.ContainsKey(id))return;
        foreach(var effect in _effects)
            if(effect.ActorId==id && effect.Definition.Toggle is not null && !enabled.Contains(effect.Profile.Id))StopToggle(effect,ActionStopReason.Policy);
    }
}
