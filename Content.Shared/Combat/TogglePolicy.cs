namespace Content.Shared.Combat;

[Flags]
public enum ToggleStopTriggers : byte { None=0, Movement=1, Skill=2, BasicAttack=4, Defense=8, EnemyInterrupt=16 }
public enum ToggleState : byte { Off, Preparing, Active }
