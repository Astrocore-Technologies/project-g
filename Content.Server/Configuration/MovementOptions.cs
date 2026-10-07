using Content.Shared.Movement;

namespace Content.Server.Configuration;

public sealed class MovementOptions
{
    public const string SectionName = "Movement";

    public float Speed { get; init; } = 5f;
    public float StopDistance { get; init; } = 0.1f;
    public float MinX { get; init; } = -15f;
    public float MaxX { get; init; } = 15f;
    public float MinZ { get; init; } = -15f;
    public float MaxZ { get; init; } = 15f;

    public MovementSettings ToSettings() =>
        new(Speed, StopDistance, MinX, MaxX, MinZ, MaxZ);
}
