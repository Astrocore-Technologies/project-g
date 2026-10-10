using System.Numerics;
namespace Content.Shared.Network;
public enum DefenseAction : byte { Block, Release, Parry, QuickRecover }
public enum DefenseOutcome : byte { Accepted, InvalidState, NoStamina, Cooldown, RateLimited }
public enum GuardImpact : byte { None, Blocked, Parried }
public readonly record struct DefenseCommand(uint Sequence, DefenseAction Action, Vector2 Direction);
public readonly record struct DefenseState(NetworkEntityId OwnerId, uint ServerTick, uint Sequence, DefenseOutcome Outcome,
    double Stamina, double MaxStamina, double BlockDamage, double ParryRemaining, double ParryCooldown,
    double DodgeCost, double ParryCost, bool Blocking, float MovementMultiplier, Vector2 Direction,
    double RecoveryRemaining = 0, double QuickRecoverCooldown = 0, double QuickRecoverCost = 15,
    float QuickRecoverRange = 1.5f, float QuickRecoverSpeed = 6, float InputBufferSeconds = .2f);
