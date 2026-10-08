namespace Content.Shared.Network;

public readonly record struct AbilityHit(ulong EffectId, NetworkEntityId ActorId, NetworkEntityId TargetId,
    uint ServerTick, double Damage, double TargetHealth);
