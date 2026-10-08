using Content.Server.Stats;

namespace Content.Server.Data;

/// <summary>File schema version is independent of numeric balance revision and network protocol.</summary>
public sealed record ContentDocument
{
    public required int SchemaVersion { get; init; }
    public required int BalanceVersion { get; init; }
    public required StatBalance Balance { get; init; }
    public required WeaponDefinition[] Weapons { get; init; }
    public required AbilityDefinition[] Abilities { get; init; }
    public required CreatureDefinition[] Creatures { get; init; }
    public required ItemDefinition[] Items { get; init; }
}
