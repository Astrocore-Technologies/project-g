namespace Content.Shared.Network;

public readonly record struct AttackResult(uint Sequence, uint ServerTick, AttackOutcome Outcome);
