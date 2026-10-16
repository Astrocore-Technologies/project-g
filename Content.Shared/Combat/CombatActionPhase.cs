namespace Content.Shared.Combat;

public enum CombatActionPhase : byte { Preparation, Activation, Active, Recovery, Finished }
public enum CombatActionForm : byte { Instant, Cast, Channel, Charged, Toggle, Hold, Passive }
public enum CombatActionCategory : byte { BasicAttack, Skill, Block, Parry, Dodge, Companion }

[Flags]
public enum ActionCapability : byte { None=0, Magical=1, Weapon=2, Movement=4 }

[Flags]
public enum ActionCancelPermission : byte
{
    None=0, Voluntary=1, Skill=2, Block=4, Parry=8, Dodge=16, BasicAttack=32, Movement=64
}
