namespace Content.Shared.Network;

public enum AttackOutcome : byte { Accepted, Cooldown, RateLimited, InvalidState, InvalidDirection }
