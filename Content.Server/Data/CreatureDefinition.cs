using System.Collections.Immutable;
using Content.Server.Stats;

namespace Content.Server.Data;

/// <summary>Immutable base definition, separate from mutable HP, AI and persistence.</summary>
public sealed record CreatureDefinition
{
    public required string Id { get; init; }
    public required BaseStats Stats { get; init; }
    public DerivedStatModifiers Modifiers { get; init; } = new();
    public required double BaseHealth { get; init; }
    public required double BaseMana { get; init; }
    public required double BaseHealthRecovery { get; init; }
    public required double BaseManaRecovery { get; init; }
    public required string WeaponId { get; init; }
    public required ImmutableArray<string> AbilityIds { get; init; }
}
