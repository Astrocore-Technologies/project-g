namespace Content.Shared.Network;

/// <summary>Revealed execution/presentation parameters for an owned slot, not a content definition.</summary>
public readonly record struct AbilityProfile(ushort Id, AbilityForm Form, float Range, float Radius, float Speed,
    double CastSeconds, double CooldownSeconds, double ManaCost, double ReadyInSeconds);
