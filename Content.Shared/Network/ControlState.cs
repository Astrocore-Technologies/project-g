using Content.Shared.Combat;

namespace Content.Shared.Network;

/// <summary>Owner summary by public control kind; instance/source identities stay on the server.</summary>
public readonly record struct ControlSummary(CrowdControlKind Kind,double RemainingSeconds,double Strength,uint Instances);
public readonly record struct ControlState(NetworkEntityId OwnerId,uint ServerTick,ControlSummary[] Controls);
