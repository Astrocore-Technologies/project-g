using System.Numerics;

namespace Content.Shared.Network;

public enum BasicAttackMode : byte { Manual, BeginContinuous, UpdateContinuous, EndContinuous }

/// <summary>Input only. Continuous repetition is server-timed; no target, approach or hit claim.</summary>
public readonly record struct AttackCommand(uint Sequence, uint ClientTick, Vector2 Direction,
    BasicAttackMode Mode = BasicAttackMode.Manual);
