namespace Content.Shared.Network;

public readonly record struct AbilityResult(uint Sequence, uint ServerTick, AbilityOutcome Outcome);
