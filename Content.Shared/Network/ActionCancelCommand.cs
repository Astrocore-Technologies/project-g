namespace Content.Shared.Network;

/// <summary>ExecutionId addresses a confirmed action; zero permits cancelling input before its acknowledgement.</summary>
public readonly record struct ActionCancelCommand(uint Sequence,uint ActionSequence,ulong ExecutionId=0);
