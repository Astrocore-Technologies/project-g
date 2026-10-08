using Content.Shared.Network;
using Godot;
using LiteNetLib;
using LiteNetLib.Utils;

namespace ProjectG.Networking;

public partial class NetworkClient
{
    public RegionEnter? CurrentRegion { get; private set; }
    public event Action? RegionChanged;
    private readonly NetDataWriter _regionWriter = new();

    private void ResetRegion(RegionEnter entry)
    {
        CurrentRegion = entry;
        KnownPlayers.Clear(); CraftRecipes.Clear(); ResourceNodes.Clear();
        Navigation = null; LatestServerTick = 0;
        LatestWorldNode = null; LatestStarterZone = null; LatestPvpZone = null;
        CanDevelopmentRevive = false;
        // Listeners detach old presentation synchronously, before the next packet in this poll.
        RegionChanged?.Invoke();
        GD.Print($"Region entered: {entry.Region}, epoch {entry.Epoch}");
    }

    private void SendGame(NetDataWriter payload, DeliveryMethod method)
    {
        if (!_handshakeComplete || _serverPeer is null) return;
        if (CurrentRegion is not { } region) { _serverPeer.Send(payload, method); return; }
        NetworkProtocol.WrapRegion(_regionWriter, region.Epoch, payload);
        _serverPeer.Send(_regionWriter, method);
    }
}
