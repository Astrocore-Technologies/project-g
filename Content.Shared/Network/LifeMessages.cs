namespace Content.Shared.Network;

public readonly record struct ActorLifeState(NetworkEntityId EntityId,uint ServerTick,ulong DeathId,bool Dead,double CorpseRemaining,double RespawnRemaining);
public readonly record struct RespawnCommand(uint Sequence,ulong DeathId);
public enum RespawnOutcome:byte { Accepted,NotReady,InvalidState,StaleDeath,AlreadyProcessed }
public readonly record struct RespawnResult(uint Sequence,uint ServerTick,RespawnOutcome Outcome);
