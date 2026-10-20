namespace Content.Shared.Network;

public enum ActivityAction:byte {AcceptDelivery,Deliver}
public enum CampWatchPhase:byte {None,Active,Completed}
public readonly record struct ActivityCommand(uint Sequence,ActivityAction Action,NetworkEntityId NpcId,ulong Order);
public readonly record struct ActivityResult(uint Sequence,uint ServerTick,ulong Order,QuestOutcome Outcome);
public readonly record struct ActivityState(NetworkEntityId OwnerId,uint ServerTick,string Title,string Objective,string Material,
    ushort Required,ushort Carried,int Experience,ulong Order,QuestStatus Status,NetworkEntityId Giver,NetworkEntityId Recipient,
    ulong WatchOccurrence,CampWatchPhase WatchPhase,int WatchExperience);
