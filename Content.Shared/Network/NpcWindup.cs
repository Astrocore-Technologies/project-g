using System.Numerics;

namespace Content.Shared.Network;

/// <summary>Revealed locked attack telegraph; zero remaining cancels/finishes it, never deals damage.</summary>
public readonly record struct NpcWindup(NetworkEntityId ActorId, uint Sequence, uint ServerTick,
    Vector2 Origin, Vector2 Direction, float Range, float RemainingSeconds);
