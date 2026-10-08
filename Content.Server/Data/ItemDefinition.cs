using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Data;

/// <summary>Server-only balance definition. A persistent instance references this ID, not a copy of its rules.</summary>
public sealed record ItemDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required EquipmentSlot Slot { get; init; }
    public string? WeaponId { get; init; }
    public Content.Server.Items.ItemConditionDefinition? Condition { get; init; }
    public DerivedStatModifiers Modifiers { get; init; } = new();
}
