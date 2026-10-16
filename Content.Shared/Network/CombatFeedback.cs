namespace Content.Shared.Network;

public enum CombatFeedbackKind:byte { PeriodicDamage, Healing, ShieldAbsorption, LifeSteal, ManaSteal, Reflection, HealthPrice, Resurrection }

/// <summary>Confirmed public result. Runtime event IDs are scoped to the current regional simulation.</summary>
public readonly record struct CombatFeedback(ulong EventId,ulong ExecutionId,NetworkEntityId SourceId,NetworkEntityId TargetId,
    uint ServerTick,CombatFeedbackKind Kind,double Amount,bool Critical);
