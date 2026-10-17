using System.Numerics;

namespace Content.Shared.Network;

public enum BasicAttackMode : byte { Manual, BeginContinuous, UpdateContinuous, EndContinuous }

/// <summary>Input only. InputOrder shares the navigation counter; Sequence remains attack-specific.
/// Continuous repetition is server-timed; no target, approach or hit claim.</summary>
public readonly record struct AttackCommand(uint Sequence, uint ClientTick, Vector2 Direction,
    BasicAttackMode Mode = BasicAttackMode.Manual,float DirectionY=0, uint InputOrder = 0)
{
    public uint InputOrder { get; init; } = InputOrder == 0 ? Sequence : InputOrder;
}
