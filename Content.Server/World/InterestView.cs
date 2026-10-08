using Content.Shared.Network;

namespace Content.Server.World;

/// <summary>Per-observer relevance state and reusable tick output. Owned by one connection.</summary>
public sealed class InterestView
{
    internal HashSet<NetworkEntityId> Candidates { get; } = new();
    internal HashSet<NetworkEntityId> Visible { get; } = new();
    internal List<NetworkEntityId> EnteredIds { get; } = new();
    internal List<NetworkEntityId> LeftIds { get; } = new();
    internal List<EntitySnapshot> States { get; } = new();
    internal AbilityInterestView Abilities { get; } = new();
    internal HashSet<NetworkEntityId> GroundCandidates { get; } = new();
    internal HashSet<NetworkEntityId> GroundVisible { get; } = new();
    internal List<NetworkEntityId> GroundEntered { get; } = new();
    internal List<NetworkEntityId> GroundLeft { get; } = new();
    internal uint NpcWindupVersion { get; set; }
    internal uint BossWindupVersion { get; set; }
    internal uint BossAreaVersion { get; set; }

    public IReadOnlyCollection<NetworkEntityId> Entities => Visible;
    public IReadOnlyList<NetworkEntityId> Entered => EnteredIds;
    public IReadOnlyList<NetworkEntityId> Left => LeftIds;
    public IReadOnlyList<EntitySnapshot> Snapshots => States;
    public IReadOnlyList<NetworkEntityId> GroundItemEntries => GroundEntered;
    public IReadOnlyList<NetworkEntityId> GroundItemExits => GroundLeft;
    public IReadOnlyCollection<NetworkEntityId> GroundItemEntities => GroundVisible;
}
