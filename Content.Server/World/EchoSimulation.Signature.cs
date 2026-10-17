using System.Numerics;
using Content.Server.Combat;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class EchoSimulation
{
    private readonly Dictionary<(NetworkEntityId Owner,byte Slot),EchoSignatureCommand> _pending=new();
    private readonly Dictionary<NetworkEntityId,EchoSignatureResult> _results=new();
    private readonly Dictionary<NetworkEntityId,List<EchoSignatureResult>> _signatureResultBatches=new();
    private readonly Dictionary<NetworkEntityId,(NetworkEntityId Owner,uint Sequence,uint ActorSequence,byte Slot,Vector3 Aim)> _signatureRequests=new();
    private readonly List<NetworkEntityId> _finishedSignatures=new();
    private readonly Dictionary<NetworkEntityId,EchoSignatureControlBuffer> _signatureControls=new();
    public IReadOnlyDictionary<NetworkEntityId,EchoSignatureResult> Results=>_results;
    internal IReadOnlyList<EchoSignatureResult> SignatureResults(NetworkEntityId owner)=>
        _signatureResultBatches.TryGetValue(owner,out var results)?results:Array.Empty<EchoSignatureResult>();
    private void SetSignatureResult(NetworkEntityId owner,EchoSignatureResult result)
    {
        _results[owner]=result;
        if(!_signatureResultBatches.TryGetValue(owner,out var batch))_signatureResultBatches.Add(owner,batch=new(NetworkConstants.MaxActiveEchoes+1));
        for(var i=0;i<batch.Count;i++)if(batch[i].Slot==result.Slot){batch[i]=result;return;}
        batch.Add(result);
    }
    private void RemovePendingSignatures(NetworkEntityId owner)
    {for(byte slot=1;slot<=NetworkConstants.MaxActiveEchoes;slot++)_pending.Remove((owner,slot));}

    public bool Queue(NetworkEntityId owner,EchoSignatureCommand command,uint tick)
    {
        if(!_owners.TryGetValue(owner,out var state) || state.Exiting || command.Sequence==0 ||
            command.Slot is <1 or >NetworkConstants.MaxActiveEchoes || !float.IsFinite(command.Aim.X) || !float.IsFinite(command.Aim.Y) ||
            !float.IsFinite(command.AimHeight) || MathF.Abs(command.AimHeight)>128 ||
            !command.EntityId.IsValid || !float.IsFinite(command.DirectionY) || MathF.Abs(command.DirectionY)>1 ||
            !float.IsFinite(command.DashDistance) || command.DashDistance is <0 or >100 || !Enum.IsDefined(command.Intent) ||
            !MovementSimulation.IsSequenceNewer(command.Sequence,state.Sequence))return false;
        state.Sequence=command.Sequence;
        if(state.SignatureRequestTicks[command.Slot-1]==tick)
        {_pending.Remove((owner,command.Slot));SetSignatureResult(owner,new(command.Sequence,tick,command.Slot,EchoCommandOutcome.RateLimited,0));return false;}
        state.SignatureRequestTicks[command.Slot-1]=tick;_pending[(owner,command.Slot)]=command;return true;
    }

    internal void PrepareFrame(uint tick)
    {
        PreparePermissions();
        foreach(var (key,command) in _pending)
        {
            var owner=key.Owner;
            var state=_owners[owner];var body=state.Slots[command.Slot-1];var master=_combat.Get(owner);
            var outcome=body is null || body.Id!=command.EntityId?EchoCommandOutcome.NotOwned:master.Health<=0 || !master.Active || state.Exiting ||
                MovementSimulation.IsSequenceNewer(command.ClientTick,tick)?EchoCommandOutcome.InvalidState:EchoCommandOutcome.Accepted;
            if(outcome==EchoCommandOutcome.Accepted&&command.Intent!=AbilityIntent.Deactivate&&!AllowsAction(body!.Id,_catalog.Abilities[body.Definition.SignatureId].NetworkId))outcome=EchoCommandOutcome.Refused;
            if(outcome!=EchoCommandOutcome.Accepted)
            {SetSignatureResult(owner,new(command.Sequence,tick,command.Slot,outcome,0));continue;}
            var skill=_catalog.Abilities[body!.Definition.SignatureId];var sequence=++body.AbilitySequence;
            if(sequence==0)sequence=++body.AbilitySequence;
            body.SignatureSequence=command.Sequence;body.SignatureActorSequence=sequence;
            _signatureRequests[body.Id]=(owner,command.Sequence,sequence,body.Slot,new(command.Aim.X,command.AimHeight,command.Aim.Y));
            if(!_abilities.QueueCompanionSignature(owner,body.Id,new(sequence,command.ClientTick,skill.NetworkId,command.Aim,
                command.DashDistance,command.AimHeight,command.DirectionY,command.Intent),tick))
            {SetSignatureResult(owner,new(command.Sequence,tick,body.Slot,EchoCommandOutcome.InvalidState,SignatureCooldown(body)));_signatureRequests.Remove(body.Id);}
        }
        _pending.Clear();
        ApplySignatureControls(tick);
    }

    public bool QueueControl(NetworkEntityId owner,EchoSignatureControlCommand command,uint tick)
    {
        if(!_owners.TryGetValue(owner,out var state)||state.Exiting||!_combat.TryGet(owner,out var master)||!master.Active||master.Health<=0 ||
            !_actors.TryGetValue(command.EntityId,out var body)||body.OwnerId!=owner||_combat.Get(body.Id).Health<=0 ||
            command.Sequence==0||command.ActionSequence==0||!Enum.IsDefined(command.Kind)||MovementSimulation.IsSequenceNewer(command.ObservedServerTick,tick)||
            !float.IsFinite(command.Aim.X)||!float.IsFinite(command.Aim.Y)||!float.IsFinite(command.AimHeight)||MathF.Abs(command.AimHeight)>128 ||
            !float.IsFinite(command.DirectionY)||MathF.Abs(command.DirectionY)>1||!MovementSimulation.IsSequenceNewer(command.Sequence,state.SignatureControlSequence))return false;
        state.SignatureControlSequence=command.Sequence;
        if(!_signatureControls.TryGetValue(owner,out var buffer))_signatureControls.Add(owner,buffer=new());
        buffer.Add(command);return true;
    }

    private void ApplySignatureControls(uint tick)
    {
        foreach(var (owner,buffer) in _signatureControls)
            foreach(var command in buffer.Commands)
            {
                if(!_owners.TryGetValue(owner,out var state)||state.Exiting||!_actors.TryGetValue(command.EntityId,out var body)||body.OwnerId!=owner ||
                    _combat.Get(owner).Health<=0||_combat.Get(body.Id).Health<=0)continue;
                uint sequence;
                if(command.ExecutionId==0)
                {
                    if(command.ActionSequence!=body.SignatureSequence)continue;
                    sequence=body.SignatureActorSequence;
                }
                else
                {
                    var action=_combat.Get(body.Id).Action;
                    if(action is null||action.Finished||action.ExecutionId!=command.ExecutionId||action.InputSequence!=command.ActionSequence||
                        action.DefinitionId!=body.Definition.SignatureId)continue;
                    sequence=action.InputSequence;
                }
                if(command.Kind==EchoSignatureControlKind.Cancel)
                {
                    var cancel=++body.CancelSequence;if(cancel==0)cancel=++body.CancelSequence;
                    _abilities.QueueCancel(body.Id,new(cancel,sequence,command.ExecutionId),tick);
                }
                else
                {
                    var control=++body.ControlSequence;if(control==0)control=++body.ControlSequence;
                    _abilities.QueueControl(body.Id,new(control,sequence,command.ObservedServerTick,
                        command.Kind==EchoSignatureControlKind.Release?AbilityControlKind.Release:AbilityControlKind.Aim,
                        command.Aim,command.AimHeight,command.DirectionY,command.ExecutionId),tick);
                }
            }
        _signatureControls.Clear();
    }

    internal AbilityLoadout SignatureLoadout(NetworkEntityId id,uint tick)
    {
        var body=_actors[id];var actor=_combat.Get(id);var skill=_catalog.Abilities[body.Definition.SignatureId].NetworkId;
        if(!_abilities.TryProfile(id,skill,out var profile))throw new InvalidOperationException("An active Echo lost its Signature.");
        return new(id,tick,actor.Resources.Mana,actor.Stats.MaxMana,[profile],body.AbilitySequence);
    }

    // Kept as the world post-frame hook; no second clock and no post-frame damage path.
    public void Simulate(float delta,uint tick)
    {
        ProcessRestorations(tick);
        CollectSummonResults(tick);
        foreach(var id in _abilities.DirtyActors)if(_actors.TryGetValue(id,out var changed)){_dirty.Add(changed.OwnerId);_loadoutDirty.Add(changed.OwnerId);}
        _finishedSignatures.Clear();
        foreach(var (id,request) in _signatureRequests)
        {
            if(!_actors.TryGetValue(id,out var body)){_finishedSignatures.Add(id);continue;}
            if(_abilities.Results.TryGetValue(id,out var result) && result.Sequence==request.ActorSequence)
            {
                if(result.Outcome==AbilityOutcome.Buffered)continue;
                SetSignatureResult(request.Owner,new(request.Sequence,tick,request.Slot,MapOutcome(result.Outcome,body,request.Aim),SignatureCooldown(body)));
                _dirty.Add(request.Owner);_loadoutDirty.Add(request.Owner);
                if(result.Outcome!=AbilityOutcome.Accepted){_finishedSignatures.Add(id);continue;}
            }
            var action=_combat.Get(id).Action;
            var running=action is {Finished:false}&&action.InputSequence==request.ActorSequence&&action.DefinitionId==body.Definition.SignatureId;
            if(!running && !_abilities.IsWaitingForSkill(id,request.ActorSequence) && !_abilities.TryExecution(id,request.ActorSequence,tick,out _))
                _finishedSignatures.Add(id);
        }
        foreach(var id in _finishedSignatures)_signatureRequests.Remove(id);
        foreach(var id in _deadBodies)
            if(_actors.TryGetValue(id,out var body))SaveBody(_owners[body.OwnerId].Entries[body.InstanceId],body,false);
    }

    private float SignatureCooldown(Actor body)
    {
        var skill=_catalog.Abilities[body.Definition.SignatureId].NetworkId;
        return _abilities.TryProfile(body.Id,skill,out var profile)?(float)profile.ReadyInSeconds:0;
    }
    private EchoCommandOutcome MapOutcome(AbilityOutcome outcome,Actor body,Vector3 aim)=>outcome switch
    {
        AbilityOutcome.Accepted=>EchoCommandOutcome.Accepted,
        AbilityOutcome.Cooldown=>EchoCommandOutcome.Cooldown,
        AbilityOutcome.InvalidAim=>Vector3.DistanceSquared(_combat.Get(body.Id).Foot,aim)>Math.Pow(_catalog.Abilities[body.Definition.SignatureId].Range,2)
            ?EchoCommandOutcome.OutOfRange:EchoCommandOutcome.Blocked,
        AbilityOutcome.RateLimited=>EchoCommandOutcome.RateLimited,
        AbilityOutcome.Cancelled or AbilityOutcome.AlreadyActive=>EchoCommandOutcome.Accepted,
        AbilityOutcome.NoMana=>EchoCommandOutcome.NoMana,
        AbilityOutcome.NoStamina=>EchoCommandOutcome.NoStamina,
        AbilityOutcome.NoHealth=>EchoCommandOutcome.NoHealth,
        AbilityOutcome.Busy=>EchoCommandOutcome.Busy,
        AbilityOutcome.NeedsSword or AbilityOutcome.NeedsWeapon=>EchoCommandOutcome.NeedsWeapon,
        AbilityOutcome.Capacity=>EchoCommandOutcome.Capacity,
        _=>!AllowsAction(body.Id,_catalog.Abilities[body.Definition.SignatureId].NetworkId)?EchoCommandOutcome.Refused:EchoCommandOutcome.InvalidState
    };
    public bool IsLoadoutDirty(NetworkEntityId owner)=>_loadoutDirty.Contains(owner);
    public EchoLoadout Loadout(NetworkEntityId owner,uint tick)
    {
        var slots=new List<EchoSlot>(NetworkConstants.MaxActiveEchoes);
        foreach(var body in _owners[owner].Slots)
            if(body is not null && _combat.Get(body.Id).Health>0)
            {var skill=_catalog.Abilities[body.Definition.SignatureId];slots.Add(new(body.Slot,body.Id,(float)skill.Range,(float)skill.Radius,SignatureCooldown(body),!AllowsAction(body.Id,skill.NetworkId)));}
        return new(owner,tick,slots);
    }
    public EchoSpawn Spawn(NetworkEntityId id,uint tick)
    {var body=_actors[id];return new(id,body.OwnerId,body.Slot,tick,body.Motion.Position,body.Definition.Name,body.Motion.Height);}
}
