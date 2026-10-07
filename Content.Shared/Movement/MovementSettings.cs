namespace Content.Shared.Movement;

public readonly record struct MovementSettings(
    float Speed,
    float StopDistance,
    float MinX,
    float MaxX,
    float MinZ,
    float MaxZ);
