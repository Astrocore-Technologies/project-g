namespace Content.Shared.Network;

/// <summary>Public restrictions; navigation height remains the ground anchor during flight.</summary>
public enum CombatControlPhase : byte { None, Airborne, KnockedDown, Displaced, Recovering }
