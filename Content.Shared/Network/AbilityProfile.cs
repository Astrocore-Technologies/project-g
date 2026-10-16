namespace Content.Shared.Network;

/// <summary>Revealed execution/presentation parameters for an owned slot, not a content definition.</summary>
public readonly record struct AbilityProfile(ushort Id, AbilityForm Form, float Range, float Radius, float Speed,
    double CastSeconds, double CooldownSeconds, double ManaCost, double ReadyInSeconds,
    double StaminaCost = 0, AbilityAvailability Availability = AbilityAvailability.Ready, AbilityArea Area = default,
    double RecoverySeconds = 0,byte Charges=1,byte MaxCharges=1,double RechargeInSeconds=0,double RechargeDuration=0,
    Content.Shared.Combat.CombatActionForm ExecutionForm=Content.Shared.Combat.CombatActionForm.Cast,
    double ChannelManaPerSecond=0,double ChannelStaminaPerSecond=0,double ChannelMaximumSeconds=0,byte PendingEndCharges=0,
    double ChargeSeconds=0,bool ChargeAutoRelease=false,bool ChargeCanTurn=false,bool SustainCanTurn=false,
    Content.Shared.Combat.ToggleState ToggleState=Content.Shared.Combat.ToggleState.Off,bool ToggleOccupiesAction=false,
    Content.Shared.Combat.ToggleStopTriggers ToggleStopOn=Content.Shared.Combat.ToggleStopTriggers.None);
