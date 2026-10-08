using Content.Shared.Network;

namespace Content.Server.World;

/// <summary>Per-observer lifecycle cache; updates only on entry, phase transition and removal.</summary>
public sealed class AbilityInterestView
{
    internal Dictionary<ulong, AbilityEffectState> Visible { get; } = new();
    internal HashSet<ulong> Current { get; } = new();
    internal List<ulong> Removed { get; } = new();
    public List<AbilityEffectState> Changes { get; } = new();
}
