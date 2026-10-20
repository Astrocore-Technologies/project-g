using System.Numerics;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private readonly Dictionary<NetworkEntityId,ActivityCommand> _activityPending=new();
    private readonly Dictionary<NetworkEntityId,(uint Sequence,uint Tick)> _activitySequences=new();
    private readonly Dictionary<NetworkEntityId,ActivityResult> _activityResults=new();
    public IReadOnlyDictionary<NetworkEntityId,ActivityResult> ActivityResults=>_activityResults;
    public bool ActivityEventDirty {get;private set;}
    public ActivityState ActivityState(NetworkEntityId id)
    {
        var def=_campServices!;var order=_progression[id].DeliveryOrder;var watch=_nodeState.CampWatch;
        return new(id,Tick,def.DeliveryTitle,def.DeliveryObjective,_progressionCatalog!.Crafting!.Materials.Single(m=>m.Id==def.MaterialId).Name,
            def.Quantity,Inventory!.QuestMaterialCount(id,def.MaterialId),def.Experience,order?.Ordinal??0,order?.Status??QuestStatus.Unknown,
            def.RegionId==RegionId?_questGiver:default,def.RegionId==RegionId?_questRecipient:default,watch?.Occurrence??0,
            watch is null?CampWatchPhase.None:watch.Active?CampWatchPhase.Active:CampWatchPhase.Completed,watch?.Experience??_experienceRewards?.WatchExperience??0);
    }
    public bool TryQueueActivity(int connection,ActivityCommand command)
    {
        if(!HasActivities||!NetworkProtocol.ValidActivityCommand(command)||!_playersByConnection.TryGetValue(connection,out var player)||!player.Loaded)return false;
        var id=player.EntityId;
        if(_activitySequences.TryGetValue(id,out var old))
        {
            if(!MovementSimulation.IsSequenceNewer(command.Sequence,old.Sequence))return false;
            if(old.Tick==Tick){_activitySequences[id]=(command.Sequence,Tick);_activityPending.Remove(id);_activityResults[id]=new(command.Sequence,Tick,command.Order,QuestOutcome.RateLimited);return false;}
        }
        GroundItems?.CancelChannel(id,Tick);_activitySequences[id]=(command.Sequence,Tick);_activityPending[id]=command;return true;
    }
    private void SimulateActivities()
    {
        foreach(var (id,command) in _activityPending)
        {
            var def=_campServices!;var player=_playersByEntity[id];var actor=Combat!.Get(id);var saved=_progression[id].DeliveryOrder;
            var expectedNpc=command.Action==ActivityAction.AcceptDelivery?_questGiver:_questRecipient;
            var outcome=QuestOutcome.Accepted;
            if(saved is not null && (saved.DefinitionId!=def.DeliveryId||saved.Revision!=def.Revision))throw new InvalidDataException("Delivery definition requires migration.");
            if(actor.Health<=0||actor.DetachedBody||!player.Loaded)outcome=QuestOutcome.InvalidState;
            else if(def.RegionId!=RegionId||!expectedNpc.IsValid||command.NpcId!=expectedNpc)outcome=QuestOutcome.Unavailable;
            else if(Combat.InCombat(id)||actor.IsCasting||player.Motion.IsMoving||player.Motion.IsDashing||Abilities!.HasActiveEffects(id)||Inventory!.IsTrading(id))outcome=QuestOutcome.Busy;
            else if(Vector3.DistanceSquared(player.Foot,GroundFoot(_questNpcs[expectedNpc].Position))>_deliveryQuest!.InteractionRange*_deliveryQuest.InteractionRange||
                !Navigation.ClearAttack(player.Foot,GroundFoot(_questNpcs[expectedNpc].Position)))outcome=QuestOutcome.TooFar;
            else if(command.Action==ActivityAction.AcceptDelivery)
            {
                if(command.Order<=(saved?.Ordinal??0))outcome=QuestOutcome.AlreadyProcessed;
                else if(saved?.Status==QuestStatus.Active||command.Order!=(saved?.Ordinal??0)+1)outcome=QuestOutcome.Unavailable;
                else{_progression[id]=_progression[id] with {DeliveryOrder=new(1,def.DeliveryId,def.Revision,command.Order,QuestStatus.Active)};DirtyProgression(id);}
            }
            else if(saved is null||command.Order>saved.Ordinal)outcome=QuestOutcome.Unavailable;
            else if(command.Order<saved.Ordinal||saved.Status==QuestStatus.Completed)outcome=QuestOutcome.AlreadyProcessed;
            else if((outcome=Inventory!.ChangeQuestMaterial(id,def.MaterialId,-def.Quantity))==QuestOutcome.Accepted)
            {
                _progression[id]=_progression[id] with {DeliveryOrder=saved with {Status=QuestStatus.Completed}};
                AwardPersonalExperience(id,new(ExperienceActivity.DeliveryCompleted,$"{def.DeliveryId}:{command.Order}",def.Revision,def.Experience));
            }
            _activityResults[id]=new(command.Sequence,Tick,command.Order,outcome);
        }
        _activityPending.Clear();
    }
    internal void ClearActivityResults(){_activityResults.Clear();ActivityEventDirty=false;}
    private void RemoveActivitySession(NetworkEntityId id){_activityPending.Remove(id);_activitySequences.Remove(id);_activityResults.Remove(id);}
}
