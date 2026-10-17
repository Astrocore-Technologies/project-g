using Content.Shared.Combat;
namespace Content.Shared.Network;

public readonly record struct AbilityHit(ulong EffectId, NetworkEntityId ActorId, NetworkEntityId TargetId,
    uint ServerTick, double Damage, double TargetHealth, GuardImpact Guard = GuardImpact.None,
    bool Critical = false, bool Evaded = false,CombatHitTags Tags=CombatHitTags.None);
