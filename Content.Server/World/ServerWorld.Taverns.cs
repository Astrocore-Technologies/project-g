using System.Numerics;
using Content.Server.Persistence;
using Content.Server.Quests;
using Content.Server.Respawn;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private TavernCatalog? _taverns;
    private readonly Dictionary<NetworkEntityId,TavernDefinition> _innkeepers=new();
    private readonly Dictionary<NetworkEntityId,SavedRespawnBinding> _respawnBindings=new();
    private readonly Dictionary<NetworkEntityId,TavernCommand> _tavernPending=new();
    private readonly Dictionary<NetworkEntityId,(uint Sequence,uint Tick)> _tavernSequences=new();
    private readonly Dictionary<NetworkEntityId,TavernResult> _tavernResults=new();
    private readonly HashSet<NetworkEntityId> _tavernDirty=new();
    internal IReadOnlyDictionary<NetworkEntityId,TavernResult> TavernResults=>_tavernResults;
    internal IReadOnlyCollection<NetworkEntityId> TavernDirty=>_tavernDirty;

    public void ConfigureTaverns(TavernCatalog catalog)
    {
        if(Players.Count!=0 || _taverns is not null)throw new InvalidOperationException("Configure taverns before accepting players.");
        _taverns=catalog;
        foreach(var definition in catalog.Taverns.Values)
        {
            if(definition.RegionId!=RegionId)continue;
            if(_questNpcs.Count>=32 || !Navigation.IsOnSurface(definition.Npc.Position)||!Navigation.IsOnSurface(definition.Spawn.Position))
                throw new InvalidDataException("Invalid tavern NPC budget or navigation.");
            var id=AllocateEntityId();_innkeepers.Add(id,definition);
            _questNpcs.Add(id,new QuestNpcDefinition(definition.Name,"innkeeper",definition.Npc.X,definition.Npc.Z));
            _spatial.Add(id,new(definition.Npc.X,definition.Npc.Z));
        }
    }

    private void RestoreRespawnBinding(NetworkEntityId id,SavedRespawnBinding? saved)
    {
        if(_taverns is null)
        {
            if(saved is not null)throw new InvalidDataException("A saved tavern requires its configured catalog.");
            return;
        }
        var binding=saved??_taverns.Starting.Binding();_taverns.Resolve(binding);
        _respawnBindings[id]=binding;_tavernDirty.Add(id);if(saved is null)MarkPersistent(id);
    }

    internal TavernDefinition? RespawnHome(NetworkEntityId id)=>_respawnBindings.TryGetValue(id,out var binding)?_taverns!.Resolve(binding):null;

    internal TavernState HomeState(NetworkEntityId id)
    {
        var home=RespawnHome(id);var penalty=_deathPenalties.GetValueOrDefault(id);
        return new(id,Tick,home?.NetworkId??0,home?.Name??"",home?.RegionId??"",penalty?.DeathId??0,penalty?.ExperienceLost??0,penalty?.Pvp??false);
    }

    internal bool TryQueueTavern(int connection,TavernCommand command)
    {
        if(command.Sequence==0||!command.NpcId.IsValid||!_playersByConnection.TryGetValue(connection,out var player)||!player.Loaded)return false;
        var id=player.EntityId;
        if(_tavernSequences.TryGetValue(id,out var previous))
        {
            if(!MovementSimulation.IsSequenceNewer(command.Sequence,previous.Sequence))return false;
            if(previous.Tick==Tick)
            {
                _tavernSequences[id]=(command.Sequence,Tick);_tavernPending.Remove(id);
                _tavernResults[id]=new(command.Sequence,Tick,TavernOutcome.RateLimited);return false;
            }
        }
        _tavernSequences[id]=(command.Sequence,Tick);_tavernPending[id]=command;return true;
    }

    private void SimulateTaverns()
    {
        foreach(var (id,command) in _tavernPending)
        {
            var actor=Combat!.Get(id);var tavern=_innkeepers.GetValueOrDefault(command.NpcId);
            var outcome=tavern is null?TavernOutcome.Unavailable:
                actor.Health<=0||!actor.Active?TavernOutcome.InvalidState:Combat.InCombat(id)?TavernOutcome.InCombat:
                Vector3.DistanceSquared(actor.Foot,tavern.Npc.Position)>tavern.InteractionRange*tavern.InteractionRange||
                !Navigation.ClearAttack(actor.Foot+Vector3.UnitY,tavern.Npc.Position+Vector3.UnitY)?TavernOutcome.TooFar:
                _respawnBindings.GetValueOrDefault(id)==tavern.Binding()?TavernOutcome.AlreadyBound:TavernOutcome.Accepted;
            if(outcome==TavernOutcome.Accepted)
            {_respawnBindings[id]=tavern!.Binding();_tavernDirty.Add(id);MarkPersistent(id);}
            _tavernResults[id]=new(command.Sequence,Tick,outcome);
        }
        _tavernPending.Clear();
    }

    private void RemoveTavernPlayer(NetworkEntityId id)
    {
        ResetRespecSession(id);
        _respawnBindings.Remove(id);_tavernPending.Remove(id);_tavernSequences.Remove(id);
        _tavernResults.Remove(id);_tavernDirty.Remove(id);_deathPenalties.Remove(id);
    }

    private void ResetTavernSession(NetworkEntityId id)
    {_tavernPending.Remove(id);_tavernSequences.Remove(id);_tavernResults.Remove(id);ResetRespecSession(id);}
}
