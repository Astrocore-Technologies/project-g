using Content.Shared.Combat;

namespace Content.Shared.Network;

public enum ControlChangeKind : byte { Applied, Refreshed, Removed, Expired, Rejected }

[Flags]
public enum ControlMitigation : byte { None=0, Immunity=1, Resistance=2, AdaptiveResistance=4 }

/// <summary>Confirmed observed change, without persistent instance/source IDs or hidden skill definitions.</summary>
public readonly record struct ControlFeedback(ulong EventId,NetworkEntityId SourceId,NetworkEntityId TargetId,uint ServerTick,
    CrowdControlKind Control,ControlChangeKind Change,double Duration,double Strength,uint RemainingInstances,
    ControlMitigation Mitigation=ControlMitigation.None);
