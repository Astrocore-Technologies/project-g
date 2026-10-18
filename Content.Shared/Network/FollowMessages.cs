namespace Content.Shared.Network;

public readonly record struct FollowCommand(uint Sequence, uint ClientTick, uint InputOrder, NetworkEntityId Target);
public enum FollowPhase : byte { Stopped, Active, Recovering, Rejected }
public enum FollowReason : byte { None, InvalidTarget, TooFar, NotVisible, Restricted, NoPath, LostTarget, TargetLeft, Died, ManualInput, Action }
public readonly record struct FollowState(uint Sequence, uint ServerTick, uint InputOrder, NetworkEntityId Target, FollowPhase Phase, FollowReason Reason);
