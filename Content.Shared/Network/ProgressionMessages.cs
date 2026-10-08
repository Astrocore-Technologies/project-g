namespace Content.Shared.Network;
public enum ProgressionAction : byte { AllocateStat, LearnSkill, AssignSlot }
public enum ProgressionOutcome : byte { Accepted, RateLimited, InvalidState, Busy, Unavailable, NoPoints }
// StatIndex: STR, AGI, VIT, INT, DEX, LUK. Zero slot removes a skill from the bar.
public readonly record struct ProgressionCommand(uint Sequence, ProgressionAction Action, byte StatIndex, ushort SkillId, byte Slot);
public readonly record struct ProgressionResult(uint Sequence, uint ServerTick, ProgressionOutcome Outcome);
public readonly record struct SkillProgress(ushort Id, int Level, int Practice, int NextPractice, byte Slot, bool Learnable);
public readonly record struct ProgressionState(NetworkEntityId OwnerId, uint ServerTick, int Level, int Experience, int NextExperience, int StatPoints, byte Discoveries, double[] Stats, SkillProgress[] Skills);
