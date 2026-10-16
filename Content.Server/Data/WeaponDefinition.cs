namespace Content.Server.Data;

/// <summary>Base content, not an owned item instance or client-provided attack result.</summary>
public sealed record WeaponDefinition
{
    public required string Id { get; init; }
    public required WeaponKind Kind { get; init; }
    public bool IsSword { get; init; }
    public Content.Shared.Combat.ActionCapability Capabilities { get; init; } = Content.Shared.Combat.ActionCapability.Weapon;
    public required double Attack { get; init; }
    public required double AttackIntervalSeconds { get; init; }
    public required double Range { get; init; }
    public HitDefense? Defense { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public HitDefense HitRules => Defense ?? (Kind == WeaponKind.Melee ? HitDefense.Melee : HitDefense.Projectile);
}
