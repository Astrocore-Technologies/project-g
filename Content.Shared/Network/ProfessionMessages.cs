namespace Content.Shared.Network;
public enum ProfessionAction : byte { Prepare, Confirm, Cancel }
public enum ProfessionOutcome : byte { Prepared, Accepted, Cancelled, Unavailable, InvalidState, Busy, RateLimited, InvalidConfirmation }
public readonly record struct ProfessionCommand(uint Sequence, ProfessionAction Action, ushort ProfessionId, uint Confirmation);
// Revealed identities plus a publicly accepted training exercise, never hidden profession counters.
public readonly record struct ProfessionState(NetworkEntityId OwnerId, uint ServerTick, ushort ActiveId, string ActiveName, ushort OfferedId, string OfferedName,
    double TrainingDamage = 0, double TrainingRequired = 0);
public readonly record struct ProfessionResult(uint Sequence, uint ServerTick, ProfessionOutcome Outcome, uint Confirmation);
