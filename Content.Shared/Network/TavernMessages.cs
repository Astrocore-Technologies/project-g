namespace Content.Shared.Network;

public readonly record struct TavernCommand(uint Sequence,NetworkEntityId NpcId);
public enum TavernOutcome:byte { Accepted,AlreadyBound,TooFar,InCombat,InvalidState,Unavailable,RateLimited }
public readonly record struct TavernResult(uint Sequence,uint ServerTick,TavernOutcome Outcome);
/// <summary>Owner-only confirmed home and last death debit. No catalog or client-selected destination.</summary>
public readonly record struct TavernState(NetworkEntityId OwnerId,uint ServerTick,ushort TavernId,string TavernName,string RegionId,
    ulong DeathId,int ExperienceLost,bool PvpDeath);
