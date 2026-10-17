using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Items;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.World;

/// <summary>Owned companion lifecycle and AI intents. Combat rules and clock belong to common runners.</summary>
public sealed partial class EchoSimulation
{
    public sealed class Actor(NetworkEntityId id,NetworkEntityId owner,Guid instance,byte slot,
        EchoCombatDefinition definition,NavigationMover motion)
    {
        public NetworkEntityId Id {get;}=id;
        public NetworkEntityId OwnerId {get;}=owner;
        public Guid InstanceId {get;}=instance;
        public byte Slot {get;}=slot;
        public NavigationMover Motion {get;}=motion;
        internal EchoCombatDefinition Definition {get;}=definition;
        internal double DecisionAt,UnreachableAt=double.NaN;
        internal uint BasicSequence,AbilitySequence;
        internal uint SignatureSequence,SignatureActorSequence,ControlSequence,CancelSequence;
        internal NetworkEntityId Target;
    }

    private sealed class Entry(SavedEchoCombat saved,IReadOnlyList<SavedCombatEffect> effects,double registeredAt,double offline)
    {
        internal readonly ulong Handle=AllocateHandle();
        internal SavedEchoCombat Saved=saved;
        internal IReadOnlyList<SavedCombatEffect> Effects=effects;
        internal double RegisteredAt=registeredAt,Offline=offline;
        internal bool LoggingIn;
        internal EchoCombatPermission Permission=EchoCombatPermission.AllowAll;
        internal Actor? Body;
    }
    private sealed class Owner(Func<NetworkEntityId> allocate)
    {
        internal readonly Dictionary<Guid,Entry> Entries=new();
        internal readonly Dictionary<ulong,Entry> Handles=new();
        internal readonly List<Entry> Roster=new();
        internal readonly Actor?[] Slots=new Actor?[NetworkConstants.MaxActiveEchoes];
        internal readonly HashSet<NetworkEntityId> Opponents=new();
        internal readonly Func<NetworkEntityId> Allocate=allocate;
        internal uint Sequence;
        internal readonly uint?[] SignatureRequestTicks=new uint?[NetworkConstants.MaxActiveEchoes];
        internal bool Exiting;
        internal uint RosterSequence,RosterPage;
        internal uint SummonSequence,RecallSequence,SummonControlSequence;
        internal uint? SummonRequestTick,RecallRequestTick,SummonControlTick;
        internal uint? RosterRequestTick;
        internal uint RestoreSequence;
        internal uint SignatureControlSequence;
        internal uint? RestoreRequestTick;
    }

    private readonly EchoOptions _options;
    private readonly ContentCatalog _catalog;
    private readonly CombatSimulation _combat;
    private readonly AbilitySimulation _abilities;
    private readonly NavigationGrid _grid;
    private readonly NavigationPathfinder _pathfinder;
    private readonly MovementSettings _movement;
    private readonly SpatialIndex _spatial;
    private readonly Func<long> _now;
    private readonly InventorySimulation? _inventory;
    private readonly IEchoCombatPermissionSource? _permissionSource;
    private readonly Dictionary<NetworkEntityId,Owner> _owners=new();
    private readonly Dictionary<NetworkEntityId,Actor> _actors=new();
    private readonly HashSet<NetworkEntityId> _awake=new(),_dirty=new(),_loadoutDirty=new(),_deadBodies=new();
    private readonly List<NetworkEntityId> _work=new(),_sleep=new();
    private static long _nextHandle;
    private static ulong AllocateHandle()
    {var handle=Interlocked.Increment(ref _nextHandle);if(handle<=0)throw new InvalidOperationException("Echo handles exhausted.");return(ulong)handle;}
    internal IReadOnlyCollection<NetworkEntityId> AwakeActors=>_awake;
    public IReadOnlyCollection<NetworkEntityId> DirtyOwners=>_dirty;
    internal string DefinitionId=>_options.DefinitionId;
    public IReadOnlyList<EchoAction> Actions=>Array.Empty<EchoAction>(); // Common attack/ability events replace legacy feedback.

    public EchoSimulation(EchoOptions options,ContentCatalog catalog,NavigationGrid grid,MovementSettings movement,
        CombatSimulation combat,AbilitySimulation abilities,SpatialIndex spatial,Func<long> now,InventorySimulation? inventory=null,
        IEchoCombatPermissionSource? permissionSource=null)
    {
        if(!catalog.Echoes.ContainsKey(options.DefinitionId) || !float.IsFinite(options.DecisionSeconds) || options.DecisionSeconds is <.05f or >2)
            throw new ArgumentException("Invalid Echo content reference or AI decision interval.");
        _options=options;_catalog=catalog;_grid=grid;_movement=movement;_combat=combat;_abilities=abilities;_spatial=spatial;_now=now;
        _pathfinder=new(grid);_inventory=inventory;_permissionSource=permissionSource;
        if(combat.CompanionActionPermission is not null)throw new InvalidOperationException("Duplicate companion permission policy.");
        combat.CompanionActionPermission=AllowsAction;
        if(combat.SummonEligibility is not null || combat.SummonActivated is not null)throw new InvalidOperationException("Duplicate companion coordinator.");
        combat.SummonEligibility=CanSummon;combat.SummonActivated=ActivateSummon;
        combat.HostileContact+=OnContact;combat.Defeated+=OnDefeated;
    }

    public bool TryGet(NetworkEntityId id,out Actor actor)=>_actors.TryGetValue(id,out actor!);
    public void Wake(NetworkEntityId owner)
    {
        if(!_owners.TryGetValue(owner,out var state))return;
        foreach(var body in state.Slots)if(body is not null && _combat.Get(body.Id).Health>0)
            if(_awake.Add(body.Id))body.DecisionAt=_combat.Time;
    }
    internal void WakeActor(NetworkEntityId id)
    {if(_actors.TryGetValue(id,out var actor)){actor.DecisionAt=_combat.Time;_awake.Add(id);}}
    internal void Respawn(NetworkEntityId owner)=>Wake(owner);
    internal void ResetSession(NetworkEntityId owner)
    {
        if(_owners.TryGetValue(owner,out var state)){state.Sequence=0;Array.Clear(state.SignatureRequestTicks);state.RosterSequence=0;state.RosterRequestTick=null;
            state.SummonSequence=state.RecallSequence=state.SummonControlSequence=state.RestoreSequence=0;state.SummonRequestTick=state.RecallRequestTick=state.SummonControlTick=state.RestoreRequestTick=null;}
        RemovePendingSignatures(owner);_results.Remove(owner);_signatureControls.Remove(owner);_signatureResultBatches.Remove(owner);
        if(state is not null)state.SignatureControlSequence=0;
        _summonRequests.Remove(owner);_summonResults.Remove(owner);_rosterDirty.Remove(owner);
        _recallResults.Remove(owner);
        _restorePending.Remove(owner);_restoreResults.Remove(owner);
    }

    private CompanionSummonOutcome CanSummon(NetworkEntityId owner,Guid instance)
    {
        if(!_owners.TryGetValue(owner,out var state) || !state.Entries.TryGetValue(instance,out var entry))return CompanionSummonOutcome.InvalidEcho;
        if(state.Exiting)return CompanionSummonOutcome.InvalidState;
        if(entry.Body is not null || entry.Saved.Availability==SavedEchoAvailability.Active)return CompanionSummonOutcome.AlreadyActive;
        if(entry.Saved.Availability!=SavedEchoAvailability.Ready)return CompanionSummonOutcome.Unavailable;
        foreach(var body in state.Slots)if(body is null)return CompanionSummonOutcome.Accepted;
        return CompanionSummonOutcome.ActiveLimit;
    }

    internal bool QueueSummon(NetworkEntityId owner,Guid instance,uint sequence,uint tick,uint inputOrder=0)
        =>_owners.ContainsKey(owner) && _combat.QueueSummon(owner,new(sequence,instance,inputOrder),tick);

    private void ActivateSummon(NetworkEntityId owner,Guid instance)
    {
        var state=_owners[owner];var entry=state.Entries[instance];
        var slot=Array.FindIndex(state.Slots,body=>body is null);
        if(slot<0)throw new InvalidOperationException("A validated summon lost its active slot.");
        var master=_combat.Get(owner);var definition=_catalog.Echoes[entry.Saved.DefinitionId];
        var preferred=master.Position+FollowOffset((byte)(slot+1),definition.FollowDistance);
        if(!TryArrival(preferred,master.Foot,definition.ArrivalRadius,out var foot))throw new InvalidOperationException("No valid Echo summon position.");
        SpawnBody(owner,entry,(byte)(slot+1),foot,loggingIn:entry.LoggingIn);
    }

    private void OnDefeated(NetworkEntityId id)
    {
        if(_actors.ContainsKey(id)){_deadBodies.Add(id);_dirty.Add(_actors[id].OwnerId);_loadoutDirty.Add(_actors[id].OwnerId);}
        ForgetOpponent(id);
    }

    public void Remove(NetworkEntityId owner)
    {
        if(!_owners.Remove(owner,out var state))return;
        ForgetOwnerOpponents(owner,state);
        foreach(var entry in state.Entries.Values)if(entry.Body is {} body)RemoveBody(body);
        RemovePendingSignatures(owner);_results.Remove(owner);_dirty.Remove(owner);_loadoutDirty.Remove(owner);_signatureControls.Remove(owner);_signatureResultBatches.Remove(owner);
        _summonRequests.Remove(owner);_summonResults.Remove(owner);_rosterDirty.Remove(owner);
        _recallResults.Remove(owner);
        _restorePending.Remove(owner);_restoreResults.Remove(owner);
    }

    private void RemoveBody(Actor body)
    {
        _signatureRequests.Remove(body.Id);_awake.Remove(body.Id);_deadBodies.Remove(body.Id);
        _abilities.FinishDeparture(body.Id);_abilities.Remove(body.Id);
        _spatial.Remove(body.Id);_actors.Remove(body.Id);_combat.Remove(body.Id);
    }

    public void ClearResults()
    {
        _work.Clear();_work.AddRange(_deadBodies);
        foreach(var id in _work)
        {
            if(!_actors.TryGetValue(id,out var body))continue;
            var owner=_owners[body.OwnerId];var entry=owner.Entries[body.InstanceId];
            SaveBody(entry,body,active:false);entry.Body=null;owner.Slots[body.Slot-1]=null;RemoveBody(body);
        }
        _deadBodies.Clear();_results.Clear();_signatureResultBatches.Clear();_dirty.Clear();_loadoutDirty.Clear();
        _summonResults.Clear();_rosterDirty.Clear();
        _recallResults.Clear();
        _restoreResults.Clear();
    }
}
