namespace Content.Shared.Network;

/// <summary>Private owner-only resource and unlocked-slot state.</summary>
public readonly record struct AbilityLoadout(NetworkEntityId EntityId, uint ServerTick,
    double Mana, double MaxMana, IReadOnlyList<AbilityProfile> Abilities);
