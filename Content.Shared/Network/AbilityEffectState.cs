using System.Numerics;

namespace Content.Shared.Network;

// AbilityId=0 is reserved for an independently flying Basic projectile (Flying/Finished only).
public readonly record struct AbilityEffectState(ulong EffectId, NetworkEntityId ActorId, uint Sequence,
    uint ServerTick, ushort AbilityId, AbilityForm Form, AbilityPhase Phase,
    Vector2 Origin, Vector2 Position, Vector2 Direction, float Radius, float Speed, float RemainingSeconds, float OriginHeight = 0, float Height = 0, float DirectionY = 0, AbilityArea Area = default,float MeleeThickness=0,float GroundVolumeHeight=0);
