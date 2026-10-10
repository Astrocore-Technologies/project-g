using Content.Shared.Network;
using Godot;
using LiteNetLib;
using LiteNetLib.Utils;

namespace ProjectG.Networking;

public partial class NetworkClient
{
    public bool RegionActive { get; private set; } = true;
    private Content.Shared.Network.SurfaceAssembly _surfaceAssembly = new();
    public RegionEnter? CurrentRegion { get; private set; }
    public event Action? RegionChanged;
    private readonly NetDataWriter _regionWriter = new();

    private void ResetRegion(RegionEnter entry)
    {
        CurrentRegion = entry; RegionActive = false; _surfaceAssembly = new();
        KnownPlayers.Clear(); CraftRecipes.Clear(); ResourceNodes.Clear();
        Navigation = null; LatestServerTick = 0;
        LatestWorldNode = null; LatestStarterZone = null; LatestPvpZone = null; LatestDefense = null;
        QuestNpcs.Clear(); LatestQuestJournal=null;
        CanDevelopmentRevive = false;
        // Listeners detach old presentation synchronously, before the next packet in this poll.
        RegionChanged?.Invoke();
        GD.Print($"Region entered: {entry.Region}, epoch {entry.Epoch}");
    }

    public void ConfirmRegionScene() => SendGame(NetworkProtocol.Write(new RegionLoadState(NetworkMessageType.RegionReady, CurrentRegion?.GeometryHash ?? 0)), DeliveryMethod.ReliableOrdered);

    private void SendGame(NetDataWriter payload, DeliveryMethod method)
    {
        if (!_handshakeComplete || _serverPeer is null) return;
        var type = (NetworkMessageType)(payload.Data[0] | payload.Data[1] << 8);
        if (!RegionActive && type is not (NetworkMessageType.RegionReady or NetworkMessageType.RegionApplied)) return;
        if (CurrentRegion is not { } region) { _serverPeer.Send(payload, method); return; }
        NetworkProtocol.WrapRegion(_regionWriter, region.Epoch, payload);
        _serverPeer.Send(_regionWriter, method);
    }
}
