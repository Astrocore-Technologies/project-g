using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class EchoSimulation
{
    private readonly Dictionary<NetworkEntityId,EchoRestoreCommand> _restorePending=new();
    private readonly Dictionary<NetworkEntityId,EchoRestoreResult> _restoreResults=new();
    public IReadOnlyDictionary<NetworkEntityId,EchoRestoreResult> RestoreResults=>_restoreResults;

    public bool QueueRestore(NetworkEntityId owner,EchoRestoreCommand command,uint tick)
    {
        if(!_owners.TryGetValue(owner,out var state)||state.Exiting||command.Sequence==0||command.Handle==0||command.DeathId==0||
            !MovementSimulation.IsSequenceNewer(command.Sequence,state.RestoreSequence)||state.RestoreRequestTick==tick)return false;
        state.RestoreSequence=command.Sequence;state.RestoreRequestTick=tick;_restorePending[owner]=command;return true;
    }

    // Post-combat: a hostile contact in this same frame must prevent out-of-combat recovery.
    private void ProcessRestorations(uint tick)
    {
        foreach(var (owner,command) in _restorePending)
        {
            var state=_owners[owner];var master=_combat.Get(owner);state.Handles.TryGetValue(command.Handle,out var entry);
            var balance=_catalog.EchoRecovery;
            var outcome=entry is null?EchoRestoreOutcome.InvalidEcho:
                !master.Active||master.Health<=0||state.Exiting||entry.Saved.Availability!=SavedEchoAvailability.RequiresRestoration?EchoRestoreOutcome.InvalidState:
                entry.Body is not null?EchoRestoreOutcome.Busy:entry.Saved.DeathId!=command.DeathId?EchoRestoreOutcome.StaleDeath:
                _combat.InCombat(owner)?EchoRestoreOutcome.InCombat:
                _inventory?.IsTrading(owner)==true?EchoRestoreOutcome.Busy:
                _inventory is null||!_inventory.CanConsume(owner,balance.SealItemId,balance.Quantity)?EchoRestoreOutcome.MissingSeal:EchoRestoreOutcome.Completed;
            if(outcome==EchoRestoreOutcome.Completed)
            {
                var prepared=PrepareReadyState(owner,state,entry!,refill:true);
                if(!_inventory!.TryConsume(owner,balance.SealItemId,balance.Quantity))throw new InvalidOperationException("Validated recovery lost its seal.");
                entry!.Saved=prepared.State;entry.Effects=prepared.Effects;entry.LoggingIn=false;entry.Offline=0;entry.RegisteredAt=_combat.Time;
                _dirty.Add(owner);_rosterDirty.Add(owner);
            }
            _restoreResults[owner]=new(command.Sequence,tick,command.Handle,outcome);
        }
        _restorePending.Clear();
    }

    private (SavedEchoCombat State,IReadOnlyList<SavedCombatEffect> Effects) PrepareReadyState(NetworkEntityId owner,Owner state,Entry entry,bool refill)
    {
        // A separate source identity prevents cleanup from removing the original Echo's independent effects on others.
        // This transient actor never enters spatial/AOI, slots or simulation; only the common restore/capture rules run.
        var id=state.Allocate();var temporary=Guid.NewGuid();var original=entry.Saved;
        var copy=original with {InstanceId=temporary,Availability=SavedEchoAvailability.Ready,ActiveSlot=null,Health=refill?double.Epsilon:original.Health};
        var effects=entry.Effects.Select(effect=>effect with {TargetEcho=temporary}).ToArray();
        var master=_combat.Get(owner);var added=false;var abilityAdded=false;
        try
        {
            _combat.Add(id,master.Position,CombatEntityKind.Echo,copy.ProfileId,copy.Stats,master.Height);added=true;
            _combat.BindCompanion(id,owner,temporary);_abilities.AddActor(id,_catalog.Creatures[copy.ProfileId]);abilityAdded=true;
            CompanionRuntimeState.Restore(_combat,_abilities,id,copy,effects,entry.Offline+_combat.Time-entry.RegisteredAt,entry.LoggingIn);
            var actor=_combat.Get(id);
            if(refill)
            {
                actor.Health=actor.Stats.MaxHealth;
                actor.Resources.Mana=Math.Max(0,actor.Stats.MaxMana);actor.Resources.Stamina=Math.Max(0,actor.Stats.MaxStamina);
            }
            var recovered=CompanionRuntimeState.Capture(_combat,_abilities,id,original.DefinitionId,original.Stats,null,_now()) with
            {InstanceId=original.InstanceId,PendingLogoutRestore=false};
            var remaining=CompanionRuntimeState.CaptureEffects(_combat,id).Select(effect=>effect with {TargetEcho=original.InstanceId}).ToArray();
            return(recovered,remaining);
        }
        finally
        {
            if(abilityAdded)_abilities.Remove(id);
            if(added)_combat.Remove(id);
        }
    }
}
