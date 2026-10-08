using System.Numerics;

namespace Content.Shared.Network;

public enum NpcAreaPhase : byte { Telegraph, Impact, Finished }

/// <summary>Public locked ground zone; damage is separate authoritative AttackEvent output.</summary>
public readonly record struct NpcArea(NetworkEntityId ActorId, uint Sequence, uint ServerTick,
    Vector2 Center, float Radius, float RemainingSeconds, NpcAreaPhase Phase);
