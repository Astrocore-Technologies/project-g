using Content.Shared.Network;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Content.Server.Networking;

public sealed partial class GameServerService
{
    private void HandleRegionLoad(NetPeer peer, NetDataReader reader, NetworkMessageType type, DeliveryMethod delivery)
    {
        if (delivery != DeliveryMethod.ReliableOrdered || !NetworkProtocol.TryReadRegionLoadState(reader, type, out var state))
        { RejectMalformed(peer, "Invalid regional readiness control."); return; }
        var world = WorldFor(peer.Id);
        if (!_loading.TryGetValue(peer.Id, out var load) || state.GeometryHash != world.GeometryHash) return;
        if (Environment.TickCount64 > load.Deadline) { peer.Disconnect(); return; }
        if (load.Stage == 0 && type == NetworkMessageType.RegionReady)
        {
            _loading[peer.Id] = (1, load.Deadline);
            SendInterest(peer, _views[peer.Id]);
            SendGame(peer, NetworkProtocol.Write(new RegionLoadState(NetworkMessageType.RegionBaseline, world.GeometryHash)), DeliveryMethod.ReliableOrdered);
        }
        else if (load.Stage == 1 && type == NetworkMessageType.RegionApplied)
        {
            world.SetLoaded(peer.Id, true); _loading.Remove(peer.Id);
            SendGame(peer, NetworkProtocol.Write(new RegionLoadState(NetworkMessageType.RegionActivated, world.GeometryHash)), DeliveryMethod.ReliableOrdered);
        }
    }
}
