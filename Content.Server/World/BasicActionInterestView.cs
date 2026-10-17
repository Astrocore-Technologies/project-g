using Content.Shared.Network;

namespace Content.Server.World;

internal sealed class BasicActionInterestView
{
    internal Dictionary<NetworkEntityId,BasicActionState> Visible {get;}=new();
    internal HashSet<NetworkEntityId> Current {get;}=new();
    internal List<NetworkEntityId> Removed {get;}=new();
    internal List<BasicActionState> Changes {get;}=new();
    internal List<BasicActionState> Poses {get;}=new();
}
