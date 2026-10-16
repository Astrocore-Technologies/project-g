using Content.Shared.Combat;
namespace Content.Shared.Network;

public readonly record struct CombatJournalRequest(uint Sequence,ulong DeathId);
public readonly record struct DeathLogSummary(ulong DeathId,long AtUnixMilliseconds,ushort EventCount);
public readonly record struct CombatJournalIndex(NetworkEntityId OwnerId,uint ServerTick,DeathLogSummary[] Deaths);
public readonly record struct CombatJournalChunk(NetworkEntityId OwnerId,uint Sequence,ulong DeathId,ushort Start,ushort Total,CombatJournalEntry[] Entries);
public enum CombatJournalOutcome:byte { NotFound,RateLimited }
public readonly record struct CombatJournalResult(uint Sequence,uint ServerTick,CombatJournalOutcome Outcome);
