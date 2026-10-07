using System.Numerics;
using Content.Shared.Movement;

namespace Content.Shared.Network;

public readonly record struct PlayerSpawn(
    PlayerId PlayerId,
    NetworkEntityId EntityId,
    Vector2 Position,
    MovementSettings Movement,
    uint ServerTick);

public readonly record struct PlayerDespawn(NetworkEntityId EntityId);

public readonly record struct MoveCommand(
    uint Sequence,
    uint ClientTick,
    Vector2 Target);

public readonly record struct EntitySnapshot(
    NetworkEntityId EntityId,
    Vector2 Position,
    uint LastProcessedSequence,
    Vector2 Target);

public readonly record struct WorldSnapshot(
    uint ServerTick,
    IReadOnlyList<EntitySnapshot> Entities);
