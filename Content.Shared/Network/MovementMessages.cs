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

public enum MovementCommandKind : byte { Cursor, Destination, Stop, CursorStart }

public readonly record struct MoveCommand(
    uint Sequence,
    uint ClientTick,
    Vector2 Target, float TargetHeight = 0, ulong GeometryHash = 0, uint InputOrder = 0,
    MovementCommandKind Kind = MovementCommandKind.Destination)
{
    // Real clients allocate this from one session counter shared with combat input.
    // Cursor refreshes keep the same order; only a new explicit destination gets another one.
    public uint InputOrder { get; init; } = InputOrder == 0 ? Sequence : InputOrder;
}

public readonly record struct EntitySnapshot(
    NetworkEntityId EntityId,
    Vector2 Position,
    uint LastProcessedSequence,
    Vector2 Target,
    uint LastAbilitySequence = 0,
    Vector2 DashDestination = default,
    float DashSpeed = 0, float Height = 0, float TargetHeight = 0, float DashHeight = 0,
    float AirOffset = 0, CombatControlPhase Control = CombatControlPhase.None, float ControlRemaining = 0,
    uint CancelledNavigationOrder = 0, uint ProcessedClientTick = 0,
    bool DirectCursor = false, bool RunningIntent = false);

public readonly record struct WorldSnapshot(
    uint ServerTick,
    IReadOnlyList<EntitySnapshot> Entities);
