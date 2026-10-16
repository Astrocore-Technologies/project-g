namespace Content.Shared.Network;

public readonly record struct LogoutCommand(uint Sequence);
public enum LogoutOutcome : byte { Accepted, InCombat }
public readonly record struct LogoutResult(uint Sequence,uint ServerTick,LogoutOutcome Outcome);
