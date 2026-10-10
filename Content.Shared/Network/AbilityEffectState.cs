using System.Numerics;

namespace Content.Shared.Network;

public readonly record struct AbilityEffectState(ulong EffectId, NetworkEntityId ActorId, uint Sequence,
    uint ServerTick, ushort AbilityId, AbilityForm Form, AbilityPhase Phase,
    Vector2 Origin, Vector2 Position, Vector2 Direction, float Radius, float Speed, float RemainingSeconds, float OriginHeight = 0, float Height = 0, float DirectionY = 0);
