using System.Numerics;

namespace Content.Shared.Network;

public enum EchoCommandOutcome : byte { Accepted, NotOwned, InvalidState, Cooldown, OutOfRange, Blocked, RateLimited }
public enum EchoActionKind : byte { BasicAttack, Signature }
public readonly record struct EchoSpawn(NetworkEntityId EntityId, NetworkEntityId OwnerId, byte Slot, uint ServerTick, Vector2 Position, string Name, float Height = 0);
public readonly record struct EchoSignatureCommand(uint Sequence, uint ClientTick, byte Slot, Vector2 Aim, float AimHeight = 0);
public readonly record struct EchoSignatureResult(uint Sequence, uint ServerTick, byte Slot, EchoCommandOutcome Outcome, float CooldownSeconds);
public readonly record struct EchoAction(NetworkEntityId EntityId, uint ServerTick, EchoActionKind Kind,
    NetworkEntityId TargetId, Vector2 Position, float Radius, double Damage, double TargetHealth, float Height = 0);
public readonly record struct EchoSlot(byte Slot, NetworkEntityId EntityId, float Range, float Radius, float CooldownSeconds);
public readonly record struct EchoLoadout(NetworkEntityId OwnerId, uint ServerTick, IReadOnlyList<EchoSlot> Slots);
