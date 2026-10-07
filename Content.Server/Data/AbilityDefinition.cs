namespace Content.Server.Data;

/// <summary>Prototype skill metadata; execution and resource validation arrive in stage 6.</summary>
public sealed record AbilityDefinition
{
    public required string Id { get; init; }
    public required AbilityKind Kind { get; init; }
    public required double Power { get; init; }
    public required double ManaCost { get; init; }
    public required double CooldownSeconds { get; init; }
    public required double CastSeconds { get; init; }
    public required double Range { get; init; }
    public required double Radius { get; init; }
}
