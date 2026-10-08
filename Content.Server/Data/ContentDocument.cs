using Content.Server.Stats;

namespace Content.Server.Data;

/// <summary>File schema version is independent of numeric balance revision and network protocol.</summary>
public sealed record ContentDocument
{
    public required int SchemaVersion { get; init; }
    public required int BalanceVersion { get; init; }
    public required StatBalance Balance { get; init; }
    public Content.Server.Progression.ProgressionBalance Progression { get; init; } = new();
    public Content.Server.Professions.ProfessionDefinition[] Professions { get; init; } = [];
    public Content.Server.WorldStory.WorldNodeDefinition? WorldNode { get; init; }
    public Content.Server.StarterZone.StarterZoneDefinition? StarterZone { get; init; }
    public required WeaponDefinition[] Weapons { get; init; }
    public required AbilityDefinition[] Abilities { get; init; }
    public required CreatureDefinition[] Creatures { get; init; }
    public Content.Server.Crafting.CraftingDefinition? Crafting { get; init; }
    public Content.Server.Pvp.PvpDefinition? Pvp {get;init;}
    public Content.Server.Economy.EconomyDefinition? Economy { get; init; }
    public required ItemDefinition[] Items { get; init; }
}
