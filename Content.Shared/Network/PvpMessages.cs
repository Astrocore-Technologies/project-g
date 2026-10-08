namespace Content.Shared.Network;
public enum PvpMode:byte {Peaceful,Voluntary,Criminal}
public enum PvpAction:byte {Mode=1,Respawn=2}
public enum PvpOutcome:byte {Accepted,AlreadyProcessed,InvalidState,NotReady,Protected,RateLimited,InvalidOperation}
public readonly record struct PvpCommand(ulong Sequence,PvpAction Action,PvpMode Mode,bool Acknowledge,ulong DeathId);
public readonly record struct PvpResult(ulong Sequence,uint ServerTick,PvpOutcome Outcome);
public readonly record struct PvpPublicState(NetworkEntityId EntityId,uint ServerTick,PvpMode Mode,bool Tagged,bool Aggressor,bool Protected,bool Dead);
public readonly record struct PvpState(NetworkEntityId OwnerId,uint ServerTick,ulong LastSequence,PvpMode Mode,int Reputation,int Pk,ulong DeathId,bool Dead,bool PvpDeath,float RespawnSeconds,int ExperienceLost,bool Dropped,bool DropSkipped,float CombatSeconds,float AggressorSeconds);
public readonly record struct PickupChannelState(NetworkEntityId OwnerId,uint ServerTick,ulong Handle,float RemainingSeconds);
public readonly record struct PvpZoneState(float MinX,float MaxX,float MinZ,float MaxZ);
public readonly record struct PvpLootState(ulong Handle,float RemainingSeconds);
