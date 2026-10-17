namespace Content.Shared.Network;

[Flags] public enum EchoRefusal : byte { None=0,Basic=1,Signature=2 }

public enum EchoAvailability : byte { Ready, Summoning, Active, RequiresRestoration }
public enum EchoSummonOutcome : byte
{ Accepted, Buffered, Completed, Cancelled, Interrupted, InvalidState, InvalidEcho, ActiveLimit, AlreadyActive, NoMana, Unavailable }
public readonly record struct EchoRosterRequest(uint Sequence,uint Page);
public readonly record struct EchoRosterEntry(ulong Handle,string Name,EchoAvailability Availability,byte Slot,
    NetworkEntityId EntityId,double Health,double Mana,double Stamina,uint ActionSequence=0,ulong Execution=0,ulong DeathId=0,EchoRefusal Refusal=EchoRefusal.None);
public readonly record struct EchoRosterPage(NetworkEntityId OwnerId,uint ServerTick,uint Sequence,uint Page,uint Total,
    IReadOnlyList<EchoRosterEntry> Entries,byte RestorationCost=1);
public readonly record struct EchoSummonCommand(uint Sequence,ulong Handle);
public readonly record struct EchoSummonResult(uint Sequence,uint ServerTick,ulong Handle,EchoSummonOutcome Outcome,ulong Execution);
public enum EchoRecallOutcome : byte { Completed,InvalidEcho,InvalidState,InCombat }
public readonly record struct EchoRecallCommand(uint Sequence,ulong Handle);
public readonly record struct EchoRecallResult(uint Sequence,uint ServerTick,ulong Handle,EchoRecallOutcome Outcome);
public readonly record struct EchoSummonControlCommand(uint Sequence,uint ActionSequence,ulong Execution=0);
public enum EchoRestoreOutcome : byte { Completed,InvalidEcho,InvalidState,InCombat,MissingSeal,StaleDeath,Busy }
public readonly record struct EchoRestoreCommand(uint Sequence,ulong Handle,ulong DeathId);
public readonly record struct EchoRestoreResult(uint Sequence,uint ServerTick,ulong Handle,EchoRestoreOutcome Outcome);
