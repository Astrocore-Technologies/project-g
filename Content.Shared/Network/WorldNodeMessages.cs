using System.Numerics;
namespace Content.Shared.Network;
public enum WorldNodeAction : byte { Repair=1, Patrol=2 }
public enum WorldNodeOutcome : byte { Accepted, AlreadyContributed, Unavailable, InvalidState, TooFar, Busy, RateLimited }
public readonly record struct WorldNodeCommand(uint Sequence,WorldNodeAction Action);
public readonly record struct WorldNodeResult(uint Sequence,uint ServerTick,WorldNodeOutcome Outcome);
/// <summary>Only public regional consequences; no private counters, conditions or operator data.</summary>
public readonly record struct WorldNodeState(ulong Revision,uint ServerTick,Vector2 Position,byte Consequences,string KeeperName,string KeeperLine,string Rumor);
