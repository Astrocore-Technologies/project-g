using System.Numerics;
using Content.Server.Data;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Bounded active effect data, no timers/tasks tied to the actor lifetime.</summary>
internal struct AbilityEffect
{
    public ulong Id;
    public NetworkEntityId ActorId;
    public uint Sequence;
    public AbilityDefinition Definition;
    public AbilityProfile Profile;
    public AbilityPhase Phase;
    public Vector2 Origin;
    public Vector2 Position;
    public Vector2 Direction;
    public float Remaining;
    public float DistanceLeft;
    public float CompensationSeconds;

    public readonly AbilityEffectState State(uint tick) => new(Id, ActorId, Sequence, tick, Profile.Id, Profile.Form,
        Phase, Origin, Position, Direction, Profile.Radius, Profile.Speed, Math.Max(0, Remaining));
}
