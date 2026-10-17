using System.Numerics;
using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class EchoSimulation
{
    public void Add(NetworkEntityId owner,SavedEchoes legacy,IReadOnlyList<SavedEchoCombat>? owned,
        IReadOnlyList<SavedCombatEffect>? effects,long savedAt,Func<NetworkEntityId> allocate,bool transfer)
    {
        legacy.Validate();var state=new Owner(allocate);var now=_now();
        if(owned is not null)
        {
            _=EchoCombatDocuments.Serialize(owned);
            foreach(var record in owned)state.Entries.Add(record.InstanceId,new(record,[],_combat.Time,record.OfflineSeconds(now)));
        }
        foreach(var old in legacy.Active)
        {
            if(state.Entries.TryGetValue(old.InstanceId,out var existing))
            {if(existing.Saved.DefinitionId!=old.DefinitionId)throw new InvalidDataException("Conflicting legacy Echo identity.");continue;}
            var record=ImportLegacy(owner,old,savedAt);
            state.Entries.Add(record.InstanceId,new(record,[],_combat.Time,record.OfflineSeconds(now)));
        }
        foreach(var entry in state.Entries.Values)
        {entry.LoggingIn=!transfer||entry.Saved.PendingLogoutRestore;state.Handles.Add(entry.Handle,entry);state.Roster.Add(entry);}
        var targets=new Dictionary<Guid,List<SavedCombatEffect>>();
        foreach(var effect in effects??[])
        {
            if(effect.TargetEcho is not {} target)continue;
            if(!state.Entries.ContainsKey(target))throw new InvalidDataException("Effect targets an unowned Echo.");
            if(!targets.TryGetValue(target,out var list))targets.Add(target,list=[]);
            list.Add(effect);
        }
        foreach(var entry in state.Entries.Values)
        {
            var record=entry.Saved;
            if(!_catalog.Echoes.TryGetValue(record.DefinitionId,out var definition) || definition.ProfileId!=record.ProfileId)
                throw new InvalidDataException("Saved Echo requires a known combat definition/profile.");
            entry.Permission=ReadPermission(entry);
            if(targets.TryGetValue(record.InstanceId,out var list))entry.Effects=list.AsReadOnly();
            if(record.Availability!=SavedEchoAvailability.Active)continue;
            if(!transfer)
            {
                // D123: a crash ends the active presence. Normalize now so deferred sustain cooldowns
                // start at login, rather than being postponed until a later summon. No resource refill.
                var ready=PrepareReadyState(owner,state,entry,refill:false);
                entry.Saved=ready.State;entry.Effects=ready.Effects;entry.LoggingIn=false;entry.Offline=0;entry.RegisteredAt=_combat.Time;
                continue;
            }
            if(!_grid.IsOnSurface(new(record.X,record.Surface?.Height??0,record.Z)) ||
                _grid.SurfaceHash!=0 && record.Surface?.GeometryHash!=_grid.SurfaceHash)
                throw new InvalidDataException("Invalid transferred Echo position.");
        }
        _owners.Add(owner,state);
        try
        {
            foreach(var entry in state.Entries.Values)
                if(entry.Saved.Availability==SavedEchoAvailability.Active)
                    SpawnBody(owner,entry,entry.Saved.ActiveSlot!.Value,new(entry.Saved.X,entry.Saved.Surface?.Height??0,entry.Saved.Z),loggingIn:false);
        }
        catch{Remove(owner);throw;}
        _dirty.Add(owner);_loadoutDirty.Add(owner);
    }

    private SavedEchoCombat ImportLegacy(NetworkEntityId owner,SavedEcho source,long savedAt)
    {
        if(!_catalog.Echoes.TryGetValue(source.DefinitionId,out var definition))throw new InvalidDataException("Unknown legacy Echo definition.");
        var profile=_catalog.Creatures[definition.ProfileId];var stats=new StatCalculator(_catalog.Balance).Calculate(profile);
        var master=_combat.Get(owner);
        return new()
        {
            InstanceId=source.InstanceId,DefinitionId=definition.Id,ProfileId=profile.Id,Availability=SavedEchoAvailability.Ready,
            Stats=profile.Stats,X=source.X??master.Position.X,Z=source.Z??master.Position.Y,Surface=source.Surface,
            Health=stats.MaxHealth,Mana=Math.Max(0,stats.MaxMana),Defense=new(1,Math.Max(0,stats.MaxStamina),0,0),
            CombatControl=new(1,0,0,0,0,0),AttackCooldownSeconds=source.AttackCooldownSeconds,
            Cooldowns=profile.AbilityIds.Select(id=>
            {
                var skill=_catalog.Abilities[id];var remaining=id==definition.SignatureId?source.SignatureCooldownSeconds:0;
                return new SavedCooldown(id,remaining,remaining>0?(byte)0:skill.MaxCharges,
                    remaining>0?Math.Max(remaining,skill.CooldownSeconds):0);
            }).ToArray(),SavedAtUnixMilliseconds=savedAt
        };
    }

    private void SpawnBody(NetworkEntityId owner,Entry entry,byte slot,Vector3 foot,bool loggingIn)
    {
        var state=_owners[owner];var record=entry.Saved;var definition=_catalog.Echoes[record.DefinitionId];
        entry.Permission=ReadPermission(entry);
        if(state.Slots[slot-1] is not null)throw new InvalidDataException("Duplicate active Echo slot.");
        var id=state.Allocate();if(!id.IsValid || _combat.TryGet(id,out _))throw new InvalidOperationException("Invalid Echo runtime allocation.");
        var body=new Actor(id,owner,record.InstanceId,slot,definition,
            new(_grid,_movement with {Speed=definition.Speed},_pathfinder,new(foot.X,foot.Z),foot.Y));
        var combatAdded=false;var abilityAdded=false;var spatialAdded=false;
        try
        {
            _combat.Add(id,body.Motion.Position,CombatEntityKind.Echo,record.ProfileId,record.Stats,foot.Y);combatAdded=true;
            _combat.BindCompanion(id,owner,record.InstanceId);
            _abilities.AddActor(id,_catalog.Creatures[record.ProfileId]);abilityAdded=true;
            CompanionRuntimeState.Restore(_combat,_abilities,id,record,entry.Effects,entry.Offline+_combat.Time-entry.RegisteredAt,loggingIn);
            _spatial.Add(id,body.Motion.Position);spatialAdded=true;
            _actors.Add(id,body);entry.Body=body;state.Slots[slot-1]=body;
            entry.LoggingIn=false;
            body.DecisionAt=_combat.Time;_awake.Add(id);_dirty.Add(owner);_loadoutDirty.Add(owner);
        }
        catch
        {
            if(spatialAdded)_spatial.Remove(id);
            if(abilityAdded)_abilities.Remove(id);
            if(combatAdded)_combat.Remove(id);
            throw;
        }
    }

    private SavedEchoCombat CaptureBody(Actor body,bool active)
    {
        var entry=_owners[body.OwnerId].Entries[body.InstanceId];
        return CompanionRuntimeState.Capture(_combat,_abilities,body.Id,body.Definition.Id,entry.Saved.Stats,
            active?body.Slot:null,_now(),_grid.SurfaceHash==0?null:new(1,body.Motion.Height,_grid.SurfaceHash));
    }

    private void SaveBody(Entry entry,Actor body,bool active)
    {
        entry.Saved=CaptureBody(body,active);entry.Effects=CompanionRuntimeState.CaptureEffects(_combat,body.Id);
        entry.RegisteredAt=_combat.Time;entry.Offline=0;
        entry.LoggingIn=false;
    }

    public SavedEchoes Capture(NetworkEntityId owner)=>SavedEchoes.Empty;
    internal IReadOnlyList<SavedEchoCombat> CaptureOwned(NetworkEntityId owner)
    {
        var state=_owners[owner];var records=new SavedEchoCombat[state.Entries.Count];var index=0;
        foreach(var entry in state.Entries.Values)records[index++]=entry.Body is {} body && !state.Exiting ? CaptureBody(body,true) :
            entry.Saved with {PendingLogoutRestore=entry.LoggingIn};
        return Array.AsReadOnly(records);
    }
    internal IReadOnlyList<SavedCombatEffect> CaptureEffects(NetworkEntityId owner)
    {
        var state=_owners[owner];var effects=new List<SavedCombatEffect>();
        foreach(var entry in state.Entries.Values)
            effects.AddRange(entry.Body is {} body && !state.Exiting?CompanionRuntimeState.CaptureEffects(_combat,body.Id):entry.Effects);
        return effects.AsReadOnly();
    }
    internal void PrepareOwnerExit(NetworkEntityId owner)
    {
        if(!_owners.TryGetValue(owner,out var state) || state.Exiting)return;
        foreach(var entry in state.Entries.Values)if(entry.Body is {} body)
        {_abilities.FinishDeparture(body.Id);SaveBody(entry,body,false);_awake.Remove(body.Id);}
        state.Exiting=true;_dirty.Add(owner);_loadoutDirty.Add(owner);
    }
    internal void OwnerCorpseRemoved(NetworkEntityId owner)
    {
        if(!_owners.TryGetValue(owner,out var state))return;
        foreach(var entry in state.Entries.Values)if(entry.Body is {} body)
        {
            _abilities.FinishDeparture(body.Id);SaveBody(entry,body,false);entry.Body=null;
            state.Slots[body.Slot-1]=null;RemoveBody(body);
        }
        _dirty.Add(owner);_loadoutDirty.Add(owner);
    }
}
