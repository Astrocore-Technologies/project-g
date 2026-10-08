using System.Reflection;
using Content.Shared.Network;
using Content.Shared.Navigation;
using Godot;
using LiteNetLib;
using LiteNetLib.Utils;

namespace ProjectG.Networking;

public partial class NetworkClient : Node
{
    [Export]
    public string DevelopmentIdentityProfile { get; set; } = "default";
    private string _identityPath = "";
    private string _identityToken = "";
    private bool _identityUsable;
    [Export]
    public bool SimulateNetworkConditions { get; set; }

    [Export(PropertyHint.Range, "0,500,1")]
    public int SimulatedMinLatencyMs { get; set; } = 100;

    [Export(PropertyHint.Range, "0,500,1")]
    public int SimulatedMaxLatencyMs { get; set; } = 150;

    [Export(PropertyHint.Range, "0,100,1")]
    public int SimulatedPacketLossPercent { get; set; }

    private EventBasedNetListener _listener = null!;
    private NetManager _client = null!;
    private NetPeer? _serverPeer;
    private bool _handshakeComplete;

    public PlayerId LocalPlayerId { get; private set; } = PlayerId.Invalid;
    public bool CanDevelopmentRevive { get; private set; }
    public ushort ServerTickRate { get; private set; } = NetworkConstants.ServerTickRate;
    public NavigationGrid? Navigation { get; private set; }
    public uint LatestServerTick { get; private set; }

    public event Action<ServerWelcome>? HandshakeCompleted;
    public event Action<PlayerSpawn>? PlayerSpawned;
    public event Action<PlayerDespawn>? PlayerDespawned;
    public event Action<WorldSnapshot>? SnapshotReceived;
    public event Action<NavigationGrid>? NavigationReceived;
    public event Action<CombatState>? CombatStateReceived;
    public event Action<AttackEvent>? AttackReceived;
    public event Action<AttackResult>? AttackResultReceived;
    public event Action<AbilityLoadout>? AbilityLoadoutReceived;
    public event Action<AbilityResult>? AbilityResultReceived;
    public event Action<AbilityEffectState>? AbilityEffectReceived;
    public event Action<AbilityHit>? AbilityHitReceived;
    public event Action<NpcWindup>? NpcWindupReceived;
    public event Action<NpcArea>? NpcAreaReceived;
    public event Action<InventoryState>? InventoryReceived;
    public event Action<InventoryResult>? InventoryResultReceived;
    public event Action<GroundItemSpawn>? GroundItemSpawned;
    public event Action<GroundItemDespawn>? GroundItemDespawned;
    public event Action<PickupResult>? PickupResultReceived;
    public event Action? Disconnected;

    public override void _Ready()
    {
        // Profiles let two local clients own different characters without sharing a credential.
        var profile = DevelopmentIdentityProfile;
        foreach (var argument in OS.GetCmdlineUserArgs())
            if (argument.StartsWith("--identity=", StringComparison.Ordinal)) profile = argument[11..];
        if (profile.Length is < 1 or > 32 || profile.Any(character =>
                character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and
                not (>= '0' and <= '9') and not '_' and not '-'))
        {
            GD.PushError("Invalid development identity profile; use 1..32 ASCII letters/digits/_/-.");
            return;
        }
        _identityPath = ProjectSettings.GlobalizePath($"user://development-identities/{profile}.token");
        try
        {
            if (System.IO.File.Exists(_identityPath))
                _identityToken = new System.IO.FileInfo(_identityPath).Length == 64
                    ? System.IO.File.ReadAllText(_identityPath) : "invalid";
            _identityUsable = NetworkProtocol.IsDevelopmentToken(_identityToken) &&
                (!System.IO.File.Exists(_identityPath) || _identityToken.Length != 0);
            if (!_identityUsable) GD.PushError("Development identity file is invalid; it was not replaced.");
        }
        catch (System.IO.IOException) { GD.PushError("Cannot read development identity; login disabled."); }
        catch (UnauthorizedAccessException) { GD.PushError("Cannot read development identity; login disabled."); }
        _listener = new EventBasedNetListener();
        _client = new NetManager(_listener);

        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.NetworkErrorEvent += OnNetworkError;

        if (SimulateNetworkConditions)
        {
            _client.SimulateLatency = true;
            _client.SimulationMinLatency = SimulatedMinLatencyMs;
            _client.SimulationMaxLatency = Math.Max(SimulatedMinLatencyMs, SimulatedMaxLatencyMs);
            _client.SimulatePacketLoss = SimulatedPacketLossPercent > 0;
            _client.SimulationPacketLossChance = SimulatedPacketLossPercent;
        }

        if (!_client.Start())
        {
            GD.PushError("Failed to start the network client.");
            return;
        }

    }

    public override void _Process(double delta) => _client?.PollEvents();

    public void ConnectToServer()
    {
        if (!_identityUsable || _client is null || _client.FirstPeer is not null)
            return;

        GD.Print("Connecting to server...");
        _client.Connect("127.0.0.1", NetworkConstants.Port, NetworkConstants.ConnectionKey);
    }

    public void SendMove(MoveCommand command)
    {
        if (!_handshakeComplete || _serverPeer is null)
            return;

        _serverPeer.Send(NetworkProtocol.Write(command), DeliveryMethod.Sequenced);
    }

    public void SendAttack(AttackCommand command)
    {
        if (_handshakeComplete && _serverPeer is not null)
            _serverPeer.Send(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered);
    }

    public void SendAbility(AbilityCommand command)
    {
        if (_handshakeComplete && _serverPeer is not null)
            _serverPeer.Send(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered);
    }
    public void SendDevelopmentRevive(DevelopmentReviveCommand command)
    {
        if (_handshakeComplete && CanDevelopmentRevive)
            _serverPeer?.Send(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered);
    }
    public void SendPickup(PickupCommand command)
    {
        if (_handshakeComplete) _serverPeer?.Send(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered);
    }
    public void SendInventory(InventoryCommand command)
    {
        if (_handshakeComplete && _serverPeer is not null)
            _serverPeer.Send(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered);
    }

    public override void _ExitTree()
    {
        if (_listener is not null)
        {
            _listener.PeerConnectedEvent -= OnPeerConnected;
            _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
            _listener.NetworkReceiveEvent -= OnNetworkReceive;
            _listener.NetworkErrorEvent -= OnNetworkError;
        }

        _client?.Stop();
    }

    private void OnPeerConnected(NetPeer peer)
    {
        _serverPeer = peer;
        var buildVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var hello = new ClientHello(NetworkConstants.ProtocolVersion, buildVersion, _identityToken);

        peer.Send(NetworkProtocol.Write(hello), DeliveryMethod.ReliableOrdered);
        GD.Print("Connected. Sending protocol handshake...");
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _serverPeer = null;
        _handshakeComplete = false;
        LocalPlayerId = PlayerId.Invalid;
        CanDevelopmentRevive = false;
        Navigation = null;
        LatestServerTick = 0;
        Disconnected?.Invoke();

        if (TryReadDisconnectRejection(disconnectInfo, out var rejection))
        {
            GD.PushError($"Server rejected connection ({rejection.Code}): {rejection.Reason}");
            return;
        }

        GD.Print($"Disconnected from server: {disconnectInfo.Reason}");
    }

    private void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        try
        {
            if (reader.AvailableBytes > NetworkConstants.MaxGamePacketBytes ||
                !NetworkProtocol.TryReadMessageType(reader, out var messageType))
            {
                DisconnectMalformed(peer);
                return;
            }

            switch (messageType)
            {
                case NetworkMessageType.ServerWelcome:
                    HandleWelcome(peer, reader);
                    break;
                case NetworkMessageType.ServerReject:
                    HandleReject(peer, reader);
                    break;
                case NetworkMessageType.PlayerSpawn:
                    if (Navigation is not null && NetworkProtocol.TryReadPlayerSpawn(reader, out var spawn) &&
                        Navigation.IsWalkable(spawn.Position))
                    {
                        if (Content.Shared.Movement.MovementSimulation.IsSequenceNewer(spawn.ServerTick, LatestServerTick))
                            LatestServerTick = spawn.ServerTick;
                        PlayerSpawned?.Invoke(spawn);
                    }
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.RegionNavigation:
                    if (_handshakeComplete && Navigation is null &&
                        NetworkProtocol.TryReadRegionNavigation(reader, out var region))
                    {
                        Navigation = new NavigationGrid(region);
                        NavigationReceived?.Invoke(Navigation);
                    }
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.PlayerDespawn:
                    if (NetworkProtocol.TryReadPlayerDespawn(reader, out var despawn))
                        PlayerDespawned?.Invoke(despawn);
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.WorldSnapshot:
                    if (NetworkProtocol.TryReadWorldSnapshot(reader, out var snapshot))
                    {
                        if (Content.Shared.Movement.MovementSimulation.IsSequenceNewer(snapshot.ServerTick, LatestServerTick))
                            LatestServerTick = snapshot.ServerTick;
                        SnapshotReceived?.Invoke(snapshot);
                    }
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.CombatState:
                    if (_handshakeComplete && Navigation is not null &&
                        NetworkProtocol.TryReadCombatState(reader, out var combat))
                        CombatStateReceived?.Invoke(combat);
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AttackEvent:
                    if (_handshakeComplete && NetworkProtocol.TryReadAttackEvent(reader, out var attack))
                        AttackReceived?.Invoke(attack);
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AttackResult:
                    if (_handshakeComplete && NetworkProtocol.TryReadAttackResult(reader, out var result))
                        AttackResultReceived?.Invoke(result);
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AbilityLoadout:
                    if (_handshakeComplete && NetworkProtocol.TryReadAbilityLoadout(reader, out var loadout))
                        AbilityLoadoutReceived?.Invoke(loadout);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AbilityResult:
                    if (_handshakeComplete && NetworkProtocol.TryReadAbilityResult(reader, out var abilityResult))
                        AbilityResultReceived?.Invoke(abilityResult);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AbilityEffectState:
                    if (_handshakeComplete && NetworkProtocol.TryReadAbilityEffectState(reader, out var effect))
                        AbilityEffectReceived?.Invoke(effect);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AbilityHit:
                    if (_handshakeComplete && NetworkProtocol.TryReadAbilityHit(reader, out var hit))
                        AbilityHitReceived?.Invoke(hit);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.NpcWindup:
                    if (_handshakeComplete && NetworkProtocol.TryReadNpcWindup(reader, out var windup))
                        NpcWindupReceived?.Invoke(windup);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.NpcArea:
                    if (_handshakeComplete && NetworkProtocol.TryReadNpcArea(reader, out var npcArea))
                        NpcAreaReceived?.Invoke(npcArea);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.InventoryState:
                    if (_handshakeComplete && NetworkProtocol.TryReadInventoryState(reader, out var inventory))
                        InventoryReceived?.Invoke(inventory);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.InventoryResult:
                    if (_handshakeComplete && NetworkProtocol.TryReadInventoryResult(reader, out var inventoryResult))
                        InventoryResultReceived?.Invoke(inventoryResult);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.DevelopmentTools:
                    if (_handshakeComplete && NetworkProtocol.TryReadDevelopmentTools(reader, out var tools)) CanDevelopmentRevive = tools.CanRevive;
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.GroundItemSpawn:
                    if (_handshakeComplete && NetworkProtocol.TryReadGroundItemSpawn(reader, out var groundSpawn)) GroundItemSpawned?.Invoke(groundSpawn);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.GroundItemDespawn:
                    if (_handshakeComplete && NetworkProtocol.TryReadGroundItemDespawn(reader, out var groundDespawn)) GroundItemDespawned?.Invoke(groundDespawn);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.PickupResult:
                    if (_handshakeComplete && NetworkProtocol.TryReadPickupResult(reader, out var pickupResult)) PickupResultReceived?.Invoke(pickupResult);
                    else DisconnectMalformed(peer);
                    break;
                default:
                    DisconnectMalformed(peer);
                    break;
            }
        }
        finally
        {
            reader.Recycle();
        }
    }

    private void HandleWelcome(NetPeer peer, NetDataReader reader)
    {
        if (_handshakeComplete ||
            !NetworkProtocol.TryReadServerWelcome(reader, out var welcome))
        {
            DisconnectMalformed(peer);
            return;
        }

        _handshakeComplete = true;
        if (welcome.DevelopmentToken.Length != 0)
        {
            if (_identityToken.Length != 0)
            {
                DisconnectMalformed(peer);
                return;
            }
            var temporary = _identityPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_identityPath)!);
                using (var stream = new System.IO.FileStream(temporary, System.IO.FileMode.CreateNew,
                           System.IO.FileAccess.Write, System.IO.FileShare.None))
                {
                    var bytes = System.Text.Encoding.ASCII.GetBytes(welcome.DevelopmentToken);
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                // Do not overwrite another concurrently launched client's profile.
                System.IO.File.Move(temporary, _identityPath, overwrite: false);
                _identityToken = welcome.DevelopmentToken;
            }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
            {
                _identityUsable = false;
                _handshakeComplete = false;
                GD.PushError("Cannot safely store development identity; connection closed. Use separate profiles for two clients.");
                _client.DisconnectPeer(peer);
                return;
            }
            finally
            {
                try { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
                catch (System.IO.IOException) { GD.PushWarning("Could not remove temporary development identity file."); }
                catch (UnauthorizedAccessException) { GD.PushWarning("Could not remove temporary development identity file."); }
            }
        }
        LocalPlayerId = welcome.PlayerId;
        ServerTickRate = welcome.TickRate;
        HandshakeCompleted?.Invoke(welcome);

        GD.Print(
            $"Handshake complete. PlayerId={LocalPlayerId.Value}, " +
            $"ServerTickRate={welcome.TickRate}.");
    }

    private void HandleReject(NetPeer peer, NetDataReader reader)
    {
        if (NetworkProtocol.TryReadServerReject(reader, out var rejection))
            GD.PushError($"Server rejected connection ({rejection.Code}): {rejection.Reason}");
        else
            GD.PushError("Server rejected connection with malformed details.");

        _client.DisconnectPeer(peer);
    }

    private void DisconnectMalformed(NetPeer peer)
    {
        GD.PushError("Received a malformed or unexpected server packet.");
        _client.DisconnectPeer(peer);
    }

    private static bool TryReadDisconnectRejection(
        DisconnectInfo disconnectInfo,
        out ServerReject rejection)
    {
        rejection = default;
        var reader = disconnectInfo.AdditionalData;

        return reader.AvailableBytes > 0 &&
               reader.AvailableBytes <= NetworkConstants.MaxHandshakePacketBytes &&
               NetworkProtocol.TryReadMessageType(reader, out var messageType) &&
               messageType == NetworkMessageType.ServerReject &&
               NetworkProtocol.TryReadServerReject(reader, out rejection);
    }

    private static void OnNetworkError(
        System.Net.IPEndPoint endpoint,
        System.Net.Sockets.SocketError error)
    {
        GD.PushWarning($"Network error from {endpoint}: {error}.");
    }
}
