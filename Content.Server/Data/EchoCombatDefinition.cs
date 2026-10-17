using System.Text;

namespace Content.Server.Data;

/// <summary>Combat content only; rarity, acquisition and relationship secrets belong to their own systems.</summary>
public sealed record EchoCombatDefinition
{
    // Server-only metadata. Combat never rolls rarity or derives relationship conditions.
    public byte? Stars {get;init;}
    public EchoRefusalActionReaction? RefusalDuringAction {get;init;}
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ProfileId { get; init; }
    public required string SignatureId { get; init; }
    public required float Speed { get; init; }
    public required float FollowDistance { get; init; }
    public required float TeleportDistance { get; init; }
    public required double UnreachableSeconds { get; init; }
    public required float ArrivalRadius { get; init; }
    public required float CombatRadius {get;init;}

    internal void Validate(ContentCatalog catalog)
    {
        if(Stars is <1 or >5 || (Stars==5)!=(RefusalDuringAction is not null) ||
            RefusalDuringAction is {} reaction && !Enum.IsDefined(reaction))
            throw new ArgumentException($"echo {Id}: 5-star combat requires an explicit refusal reaction; other rarities cannot refuse.");
        if (Name is not {Length: > 0 and <= 24} || Encoding.UTF8.GetByteCount(Name) > 48 ||
            ProfileId is null || !catalog.Creatures.TryGetValue(ProfileId, out var profile) ||
            SignatureId is null || !profile.AbilityIds.Contains(SignatureId) ||
            !catalog.Abilities.TryGetValue(SignatureId, out var signature) || !signature.ManualOnly || signature.Passive is not null ||
            !float.IsFinite(Speed) || Speed <= 0 || !float.IsFinite(FollowDistance) || FollowDistance <= 0 ||
            !float.IsFinite(TeleportDistance) || TeleportDistance <= FollowDistance ||
            !double.IsFinite(UnreachableSeconds) || UnreachableSeconds <= 0 ||
            !float.IsFinite(CombatRadius) || CombatRadius<=0 || CombatRadius>TeleportDistance ||
            !float.IsFinite(ArrivalRadius) || ArrivalRadius <= 0 || ArrivalRadius >= TeleportDistance)
            throw new ArgumentException($"echo {Id}: invalid combat profile, manual Signature or follow policy.");
    }
}

// Interrupt uses the existing interruptible-action/payment rules, explicitly selected by this Echo's author.
public enum EchoRefusalActionReaction : byte { FinishCurrent, InterruptAsEnemy }
