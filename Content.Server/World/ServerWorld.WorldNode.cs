using System.Numerics;
using Content.Database;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.WorldStory;
using Content.Shared.Movement;
using Content.Shared.Network;
namespace Content.Server.World;
public sealed partial class ServerWorld
{
    private WorldNodeDefinition? _node;
    private SavedWorldNode _nodeState=new();
    private long _nodeRevision=1;
    private byte _appliedConsequences;
    private readonly Dictionary<int,WorldNodeCommand> _nodePending=new();
    private readonly Dictionary<int,(uint Sequence,uint Tick)> _nodeSequences=new();
    private readonly Dictionary<int,string> _persistentActors=new();
    private readonly Dictionary<NetworkEntityId,WorldNodeResult> _nodeResults=new();
    private readonly List<DatabaseWorldAudit> _nodeAudit=new(8);
    public bool HasWorldNode => _node is not null;
    public string WorldNodeKey => _node!.Key;
    public long WorldNodeRevision => _nodeRevision;
    public bool WorldNodeDirty => _nodeAudit.Count!=0;
    public IReadOnlyDictionary<NetworkEntityId,WorldNodeResult> WorldNodeResults => _nodeResults;
    public IReadOnlyList<DatabaseWorldAudit> WorldNodeAudit => _nodeAudit;
    public SavedWorldNode CaptureWorldNode() => _nodeState;
    private byte Consequences => _node is null ? (byte)0 : (byte)((_nodeState.Repairs>=_node.ContributionsRequired ? 1 : 0) | (_nodeState.Patrols>=_node.ContributionsRequired ? 2 : 0));
    private void InitializeWorldNode(WorldStoryOptions? options,WorldNodeDefinition? definition)
    {
        if (options?.Enabled!=true) return;
        _node=definition ?? throw new InvalidDataException("World node definition is missing."); _node.Validate();
        if (Npc is null || !Navigation.IsWalkable(_node.Position) || _node.OpeningCells.Any(i=>i>=Navigation.CellCount || !Navigation.IsBlocked(i%Navigation.Width,i/Navigation.Width)) || _node.PatrolAggroRadius>Npc.EffectiveAggroRadius) throw new InvalidDataException("World node is incompatible with region/NPC.");
    }
    public void RestoreWorldNode(SavedWorldNode state,long revision)
    {
        if (!HasWorldNode || WorldNodeDirty || _appliedConsequences!=0 || revision<=0) throw new InvalidOperationException("World restore is startup-only.");
        state.Validate(); if(state.Repairs>_node!.ContributionsRequired || state.Patrols>_node.ContributionsRequired) throw new InvalidDataException("World content migration required.");
        _nodeState=state; _nodeRevision=revision; RestoreCraftResources(); ApplyWorldConsequences();
    }
    public void BindWorldActor(int connection,Guid character)
    {
        if(character==Guid.Empty || !_playersByConnection.ContainsKey(connection)) throw new ArgumentException("Invalid persistent actor.");
        _persistentActors[connection]=character.ToString("N");
    }
    public WorldNodeState PublicWorldNode() => new((ulong)_nodeRevision,Tick,_node!.Position,Consequences,_node.KeeperName,_node.KeeperLines[Consequences],_node.Rumors[_nodeState.StormRumor ? 4 : Consequences]);
    public bool TryQueueWorldNode(int connection,WorldNodeCommand command)
    {
        if(!HasWorldNode || !_playersByConnection.TryGetValue(connection,out var p) || command.Sequence==0 || !Enum.IsDefined(command.Action)) return false;
        if(_nodeSequences.TryGetValue(connection,out var previous))
        {
            if(!MovementSimulation.IsSequenceNewer(command.Sequence,previous.Sequence)) return false;
            if(previous.Tick==Tick) { _nodeSequences[connection]=(command.Sequence,Tick); _nodePending.Remove(connection); _nodeResults[p.EntityId]=new(command.Sequence,Tick+1,WorldNodeOutcome.RateLimited); return false; }
        }
        _nodeSequences[connection]=(command.Sequence,Tick); _nodePending[connection]=command; return true;
    }
    private void SimulateWorldNode()
    {
        if(_node is null) return;
        foreach(var action in Combat!.Events) if(action.Damage>0 && Npc!.Id==action.TargetId) RecordPatrolHit(action.AttackerId);
        foreach(var hit in Abilities!.Hits) if(hit.Damage>0 && Npc!.Id==hit.TargetId) RecordPatrolHit(hit.ActorId);
        foreach(var (connection,command) in _nodePending)
        {
            var p=_playersByConnection[connection]; var actor=Combat.Get(p.EntityId); var history=_progression[p.EntityId].WorldParticipation;
            var bit=(byte)command.Action; var outcome=WorldNodeOutcome.Accepted;
            if(actor.Health<=0) outcome=WorldNodeOutcome.InvalidState;
            else if(actor.IsCasting || p.Motion.IsDashing || p.Motion.IsMoving) outcome=WorldNodeOutcome.Busy;
            else if(Vector2.DistanceSquared(p.Position,_node.Position)>_node.InteractionRange*_node.InteractionRange || !Navigation.CanTraverse(p.Position,_node.Position)) outcome=WorldNodeOutcome.TooFar;
            else if((history.Contributions&bit)!=0) outcome=WorldNodeOutcome.AlreadyContributed;
            else if((Consequences&bit)!=0 || command.Action==WorldNodeAction.Patrol && history.PatrolHits<_node.PatrolHitsRequired) outcome=WorldNodeOutcome.Unavailable;
            if(outcome==WorldNodeOutcome.Accepted)
            {
                _progression[p.EntityId]=_progression[p.EntityId] with { WorldParticipation=history with { Contributions=(byte)(history.Contributions|bit) } }; MarkPersistent(p.EntityId);
                _nodeState=command.Action==WorldNodeAction.Repair ? _nodeState with { Repairs=_nodeState.Repairs+1 } : _nodeState with { Patrols=_nodeState.Patrols+1 };
                Audit(_persistentActors.GetValueOrDefault(connection,"runtime-"+connection),command.Action.ToString(),"Player contribution"); ApplyWorldConsequences();
            }
            _nodeResults[p.EntityId]=new(command.Sequence,Tick,outcome);
        }
        _nodePending.Clear();
    }
    private void RecordPatrolHit(NetworkEntityId id)
    {
        if(!_progression.TryGetValue(id,out var progression) || progression.WorldParticipation.PatrolHits>=_node!.PatrolHitsRequired) return;
        _progression[id]=progression with { WorldParticipation=progression.WorldParticipation with { PatrolHits=progression.WorldParticipation.PatrolHits+1 } }; MarkPersistent(id);
    }
    private void ApplyWorldConsequences()
    {
        var consequences=Consequences;
        if((consequences&1)!=0 && (_appliedConsequences&1)==0)
        {
            var opened=Navigation.ToMessage(); foreach(var cell in _node!.OpeningCells) opened.BlockedCells[cell]=0; Navigation.ApplyOpening(opened);
        }
        if((consequences&2)!=0 && (_appliedConsequences&2)==0) Npc!.ApplyPatrol(_node!.PatrolAggroRadius,Tick);
        _appliedConsequences=consequences;
    }
    internal void ApplyLiveDm(AuthorizedLiveDm command)
    {
        if(_node is null) return;
        _nodeState=command.Operation switch
        {
            LiveDmOperation.OpenBridge => _nodeState with { Repairs=_node.ContributionsRequired },
            LiveDmOperation.EstablishPatrol => _nodeState with { Patrols=_node.ContributionsRequired },
            LiveDmOperation.StormRumor => _nodeState with { StormRumor=true },
            _ => throw new ArgumentException("Unknown prepared operation.")
        };
        Audit("dm:"+command.Actor,command.Operation.ToString(),command.Reason); ApplyWorldConsequences();
    }
    private void Audit(string actor,string operation,string reason) => _nodeAudit.Add(new(actor,operation,reason,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    public void CommitWorldNode(long revision)
    {
        if(!WorldNodeDirty || revision!=_nodeRevision+1) throw new InvalidOperationException("World revision mismatch.");
        _nodeRevision=revision; _nodeAudit.Clear();
    }
    public void ClearWorldNodeResults() => _nodeResults.Clear();
    private void RemoveWorldNodePlayer(int connection,NetworkEntityId id)
    { _nodePending.Remove(connection); _nodeSequences.Remove(connection); _nodeResults.Remove(id); _persistentActors.Remove(connection); }
}
