using System.Numerics;
namespace Content.Shared.Network;
public enum DefenseAction : byte { Block, Release, Parry }
public enum DefenseOutcome : byte { Accepted, InvalidState, NoStamina, Cooldown, RateLimited }
public enum GuardImpact : byte { None, Blocked, Parried }
public readonly record struct DefenseCommand(uint Sequence, DefenseAction Action, Vector2 Direction);
public readonly record struct DefenseState(NetworkEntityId OwnerId, uint ServerTick, uint Sequence, DefenseOutcome Outcome,
    double Stamina, double MaxStamina, double BlockDamage, double ParryRemaining, double ParryCooldown,
    double DodgeCost, double ParryCost, bool Blocking, float MovementMultiplier, Vector2 Direction);
