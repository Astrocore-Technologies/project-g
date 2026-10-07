namespace Content.Server.Data;

/// <summary>Base content, not an owned item instance or client-provided attack result.</summary>
public sealed record WeaponDefinition
{
    public required string Id { get; init; }
    public required WeaponKind Kind { get; init; }
    public required double Attack { get; init; }
    public required double AttackIntervalSeconds { get; init; }
    public required double Range { get; init; }
}
