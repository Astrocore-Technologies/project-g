namespace Content.Shared.Network;
public enum ProgressionAction : byte { AllocateStat, LearnSkill, AssignSlot, PreviewStats, AllocateStats }
public enum ProgressionOutcome : byte { Accepted, RateLimited, InvalidState, Busy, Unavailable, NoPoints }
// StatIndex: STR, AGI, VIT, INT, DEX, LUK. Zero slot removes a skill from the bar.
public readonly record struct ProgressionCommand(uint Sequence, ProgressionAction Action, byte StatIndex, ushort SkillId, byte Slot, int[]? Allocation = null);
// Owner-only display values in the order documented by CharacterStat. No balance formulas leave the server.
public enum CharacterStat : byte { MeleeAttack, RangedAttack, MagicAttack, MeleeWeapon, RangedWeapon, PhysicalDefense, MagicDefense, MaxHealth, MaxMana, HealthRecovery, ManaRecovery, HealthItem, ManaItem, AttackSpeed, CastSpeed, CriticalChance, BlockDamage }
public readonly record struct StatPreview(uint Sequence, uint ServerTick, double[] Current, double[] Projected);
public readonly record struct ProgressionResult(uint Sequence, uint ServerTick, ProgressionOutcome Outcome);
public readonly record struct SkillProgress(ushort Id, int Level, int Practice, int NextPractice, byte Slot, bool Learnable);
public readonly record struct ProgressionState(NetworkEntityId OwnerId, uint ServerTick, int Level, int Experience, int NextExperience, int StatPoints, byte Discoveries, double[] Stats, SkillProgress[] Skills);
