using System.Numerics;
using Content.Shared.Movement;

namespace Content.Shared.Network;

public readonly record struct PlayerSpawn(
    PlayerId PlayerId,
    NetworkEntityId EntityId,
    Vector2 Position,
    MovementSettings Movement,
    uint ServerTick, float Height = 0);

public readonly record struct PlayerDespawn(NetworkEntityId EntityId);

public readonly record struct MoveCommand(
    uint Sequence,
    uint ClientTick,
    Vector2 Target, float TargetHeight = 0, ulong GeometryHash = 0);

public readonly record struct EntitySnapshot(
    NetworkEntityId EntityId,
    Vector2 Position,
    uint LastProcessedSequence,
    Vector2 Target,
    uint LastAbilitySequence = 0,
    Vector2 DashDestination = default,
    float DashSpeed = 0, float Height = 0, float TargetHeight = 0, float DashHeight = 0);

public readonly record struct WorldSnapshot(
    uint ServerTick,
    IReadOnlyList<EntitySnapshot> Entities);
