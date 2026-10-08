using System.Diagnostics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Server.Persistence;
using Content.Shared.Network;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Content.Server.Networking;

/// <summary>
/// Owns transport and advances the authoritative world on a fixed tick.
/// All callbacks and simulation updates execute on this service's single thread.
/// </summary>
public sealed partial class GameServerService : BackgroundService
{
    private readonly ServerOptions _options;
    private readonly HandshakeCoordinator _handshakes;
    private readonly ServerWorld _world;
    private readonly ILogger<GameServerService> _logger;
    private readonly EventBasedNetListener _listener = new();
    private readonly Dictionary<int, NetPeer> _peers = new();
    private readonly Dictionary<int, InterestView> _views = new();
    private readonly NetDataWriter _snapshotWriter = new();
    private readonly NetManager _server;
    private readonly bool _developmentRevive;

    public GameServerService(
        IOptions<ServerOptions> options,
        HandshakeCoordinator handshakes,
        ServerWorld world,
        ILogger<GameServerService> logger,
        ICharacterStore? characters = null,
        IOptions<PersistenceOptions>? persistence = null, IHostEnvironment? environment = null)
    {
        _options = options.Value;
        _handshakes = handshakes;
        _world = world;
        _characters = characters;
        _developmentRevive = environment?.IsDevelopment() == true;
        _maxSessions = persistence?.Value.MaxSessions ?? 32;
        if (_maxSessions is < 1 or > 64) throw new ArgumentException("Development session budget must be 1..64.");
        _logger = logger;
        _server = new NetManager(_listener);

        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.NetworkErrorEvent += OnNetworkError;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_characters is not null) await _characters.InitializeAsync(stoppingToken);
        if (_world.GroundItems is { } ground)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            ground.Restore(_characters is null ? ground.Seeds : await _characters.LoadGroundItemsAsync(ground.Seeds, deadline.Token));
        }
        if (!_server.Start(_options.Port))
            throw new InvalidOperationException($"Failed to start UDP server on port {_options.Port}.");

        _logger.LogInformation(
            "Server started. Port={Port}, Protocol={ProtocolVersion}, TickRate={TickRate}",
            _options.Port,
            NetworkConstants.ProtocolVersion,
            _options.TickRate);

        var fixedDelta = 1f / _options.TickRate;
        var tickDuration = TimeSpan.FromSeconds(fixedDelta);
        var clock = Stopwatch.StartNew();
        var nextTick = clock.Elapsed + tickDuration;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _server.PollEvents();
                CompleteCheckpoint();
                if (_checkpoint is null)
                {
                    CompleteLogins();
                    CompleteClosings();
                    CloseDepartedPlayers();
                }

                // Limit catch-up work so a temporary stall cannot spiral indefinitely.
                for (var catchUp = 0; _checkpoint is null && clock.Elapsed >= nextTick && catchUp < 4; catchUp++)
                {
                    ApplyBufferedIntentions();
                    _world.Simulate(fixedDelta);
                    if (!BeginCheckpoint()) BroadcastSnapshot();
                    nextTick += tickDuration;
                }

                await Task.Delay(_options.NetworkPollIntervalMilliseconds, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        finally
        {
            _server.Stop();
            await FlushCharactersAsync();
            _logger.LogInformation("Server stopped cleanly");
        }
    }

    public override void Dispose()
    {
        _listener.ConnectionRequestEvent -= OnConnectionRequest;
        _listener.PeerConnectedEvent -= OnPeerConnected;
        _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
        _listener.NetworkReceiveEvent -= OnNetworkReceive;
        _listener.NetworkErrorEvent -= OnNetworkError;
        _server.Stop();
        base.Dispose();
    }

    private void OnConnectionRequest(ConnectionRequest request) =>
        request.AcceptIfKey(_options.ConnectionKey);

    private void OnPeerConnected(NetPeer peer)
    {
        _peers[peer.Id] = peer;
        _handshakes.RegisterConnection(peer.Id);
        _logger.LogInformation(
            "Peer connected. ConnectionId={ConnectionId}, Address={Address}",
            peer.Id,
            peer.Address);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _peers.Remove(peer.Id);
        _views.Remove(peer.Id);
        var playerId = _handshakes.RemoveConnection(peer.Id);
        _intentions.Remove(peer.Id);
        if (_sessions.ContainsKey(peer.Id)) _departed.Add(peer.Id);
        else _world.RemovePlayer(peer.Id);

        // Remaining observers receive the despawn through their AOI delta next tick.

        _logger.LogInformation(
            "Peer disconnected. ConnectionId={ConnectionId}, PlayerId={PlayerId}, Reason={Reason}",
            peer.Id,
            playerId.Value,
            disconnectInfo.Reason);
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
                RejectMalformed(peer, "Packet is malformed or too large.");
                return;
            }

            if (!_handshakes.TryGetPlayerId(peer.Id, out _))
            {
                if (reader.RawDataSize > NetworkConstants.MaxHandshakePacketBytes || deliveryMethod != DeliveryMethod.ReliableOrdered)
                {
                    RejectMalformed(peer, "Handshake must be bounded and reliable.");
                    return;
                }
                HandleHandshake(peer, reader, messageType);
                return;
            }

            // Identity loading is asynchronous; no player intentions before its restored spawn.
            if (_characters is not null && !_views.ContainsKey(peer.Id)) return;

            if (messageType == NetworkMessageType.MoveCommand &&
                NetworkProtocol.TryReadMoveCommand(reader, out var command))
            {
                if (_characters is null) _world.TryApplyMove(peer.Id, command);
                else BufferIntentions(peer.Id).Move = command;
                return;
            }
            if (messageType == NetworkMessageType.AttackCommand &&
                deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadAttackCommand(reader, out var attack))
            {
                if (_characters is null) _world.TryQueueAttack(peer.Id, attack);
                else BufferIntentions(peer.Id).Attack ??= attack;
            }
            if (messageType == NetworkMessageType.AbilityCommand &&
                deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadAbilityCommand(reader, out var ability))
            {
                if (_characters is null) _world.TryQueueAbility(peer.Id, ability, peer.Ping);
                else BufferIntentions(peer.Id).Ability ??= ability;
            }
            if (messageType == NetworkMessageType.InventoryCommand && deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadInventoryCommand(reader, out var inventory))
            {
                if (_characters is null) _world.TryQueueInventory(peer.Id, inventory);
                else BufferIntentions(peer.Id).Inventory ??= inventory;
            }
            if (messageType == NetworkMessageType.PickupCommand && deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadPickupCommand(reader, out var pickup))
            {
                if (_characters is null) _world.TryQueuePickup(peer.Id, pickup);
                else BufferIntentions(peer.Id).Pickup ??= pickup;
            }
            if (messageType == NetworkMessageType.DevelopmentRevive && CanDevelopmentRevive(peer) &&
                deliveryMethod == DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadDevelopmentRevive(reader, out var revive))
            {
                if (_characters is null) _world.TryQueueDevelopmentRevive(peer.Id, revive, authorized: true);
                else BufferIntentions(peer.Id).Revive ??= revive;
            }
            if (messageType == NetworkMessageType.EchoSignatureCommand && deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadEchoSignatureCommand(reader,out var echo))
            {
                if (_characters is null) _world.TryQueueEchoSignature(peer.Id,echo);
                else BufferIntentions(peer.Id).Echo ??= echo;
            }
            // Invalid game intentions are discarded; avoid logging unbounded client spam.
        }
        finally
        {
            reader.Recycle();
        }
    }

    private bool CanDevelopmentRevive(NetPeer peer) => _developmentRevive && System.Net.IPAddress.IsLoopback(peer.Address);

    private void HandleHandshake(
        NetPeer peer,
        NetDataReader reader,
        NetworkMessageType messageType)
    {
        if (messageType != NetworkMessageType.ClientHello)
        {
            Reject(peer, HandshakeRejectCode.UnexpectedMessage, "ClientHello was expected.");
            return;
        }

        if (!NetworkProtocol.TryReadClientHello(reader, out var hello))
        {
            RejectMalformed(peer, "ClientHello is malformed.");
            return;
        }

        var decision = _handshakes.ProcessHello(peer.Id, hello);
        if (!decision.IsAccepted)
        {
            Reject(peer, decision.RejectCode, decision.RejectReason);
            return;
        }

        if (_characters is not null)
        {
            BeginLogin(peer, hello, decision.PlayerId);
            return;
        }

        AcceptPlayer(peer, hello, decision.PlayerId);
    }

    private void AcceptPlayer(NetPeer peer, ClientHello hello, PlayerId playerId, CharacterSession? session = null)
    {
        var player = _world.AddPlayer(peer.Id, playerId, session?.State);
        var welcome = new ServerWelcome(
            playerId,
            checked((ushort) _options.TickRate),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session?.IssuedToken ?? "");

        peer.Send(NetworkProtocol.Write(welcome), DeliveryMethod.ReliableOrdered);
        peer.Send(NetworkProtocol.Write(new DevelopmentTools(CanDevelopmentRevive(peer))), DeliveryMethod.ReliableOrdered);
        // Public collision geometry arrives before any spawn on the same reliable stream.
        peer.Send(NetworkProtocol.Write(_world.Navigation.ToMessage()), DeliveryMethod.ReliableOrdered);

        var view = new InterestView();
        _views.Add(peer.Id, view);
        SendInterest(peer, view);

        _logger.LogInformation(
            "Handshake accepted. ConnectionId={ConnectionId}, PlayerId={PlayerId}, EntityId={EntityId}, Build={BuildVersion}",
            peer.Id,
            playerId.Value,
            player.EntityId.Value,
            hello.BuildVersion);
    }

    private void BroadcastSnapshot()
    {
        if (_world.Players.Count == 0)
            return;

        foreach (var (connectionId, peer) in _peers)
        {
            if (_views.TryGetValue(connectionId, out var view))
                SendInterest(peer, view);
        }
        _world.Combat?.ClearResults();
        _world.Abilities?.ClearResults();
        _world.Inventory?.ClearResults();
        _world.GroundItems?.ClearResults();
        _world.Echoes?.ClearResults();
        if (_characters is null) _world.GroundItems?.CommitClaims();
    }

    private void SendInterest(NetPeer peer, InterestView view)
    {
        _world.UpdateInterest(peer.Id, view);
        _world.UpdateGroundInterest(peer.Id, view);
        if (_world.GroundItems is { } ground)
        {
            foreach (var id in view.GroundLeft)
                peer.Send(NetworkProtocol.Write(new GroundItemDespawn(id.Value, _world.Tick)), DeliveryMethod.ReliableOrdered);
            foreach (var id in view.GroundEntered)
                peer.Send(NetworkProtocol.Write(ground.State(id.Value, _world.Tick)), DeliveryMethod.ReliableOrdered);
        }
        foreach (var id in view.Left)
            peer.Send(NetworkProtocol.Write(new PlayerDespawn(id)), DeliveryMethod.ReliableOrdered);
        foreach (var id in view.Entered)
        {
            if (_world.Echoes?.TryGet(id,out _) == true)
            {
                peer.Send(NetworkProtocol.Write(_world.Echoes.Spawn(id,_world.Tick)),DeliveryMethod.ReliableOrdered);
                continue;
            }
            if (_world.IsPlayer(id))
                peer.Send(NetworkProtocol.Write(_world.CreateSpawn(id)), DeliveryMethod.ReliableOrdered);
            if (_world.Combat is { } combat)
                peer.Send(NetworkProtocol.Write(combat.State(id, _world.Tick)), DeliveryMethod.ReliableOrdered);
        }

        if (_world.Combat is { } equipmentCombat)
            foreach (var id in equipmentCombat.EquipmentDirty)
                if (view.Entities.Contains(id) && !view.Entered.Contains(id))
                    peer.Send(NetworkProtocol.Write(equipmentCombat.State(id, _world.Tick)), DeliveryMethod.ReliableOrdered);
        if (_world.Inventory is { } inventory && _world.TryGetOwnedEntity(peer.Id, out var inventoryOwner))
        {
            if (view.Entered.Contains(inventoryOwner) || inventory.IsDirty(inventoryOwner))
                peer.Send(NetworkProtocol.Write(inventory.State(inventoryOwner, _world.Tick)), DeliveryMethod.ReliableOrdered);
            if (inventory.Results.TryGetValue(inventoryOwner, out var inventoryResult))
                peer.Send(NetworkProtocol.Write(inventoryResult), DeliveryMethod.ReliableOrdered);
        }
        if (_world.GroundItems is { } pickups && _world.TryGetOwnedEntity(peer.Id, out var pickupOwner) &&
            pickups.Results.TryGetValue(pickupOwner, out var pickupResult))
            peer.Send(NetworkProtocol.Write(pickupResult), DeliveryMethod.ReliableOrdered);
        if (_world.Echoes is { } echoes)
        {
            if (_world.TryGetOwnedEntity(peer.Id,out var owner))
            {
                if (view.Entered.Contains(owner) || echoes.IsLoadoutDirty(owner))
                    peer.Send(NetworkProtocol.Write(echoes.Loadout(owner,_world.Tick)),DeliveryMethod.ReliableOrdered);
                if (echoes.Results.TryGetValue(owner,out var echoResult))
                    peer.Send(NetworkProtocol.Write(echoResult),DeliveryMethod.ReliableOrdered);
            }
        }
        if (_world.Abilities is { } abilities)
        {
            _world.UpdateAbilityInterest(peer.Id, view, view.Abilities);
            foreach (var effect in view.Abilities.Changes)
                peer.Send(NetworkProtocol.Write(effect), DeliveryMethod.ReliableOrdered);
            foreach (var hit in abilities.Hits)
            {
                if (view.Entities.Contains(hit.ActorId) && view.Entities.Contains(hit.TargetId))
                    peer.Send(NetworkProtocol.Write(hit), DeliveryMethod.ReliableOrdered);
                else if (view.Entities.Contains(hit.TargetId))
                    peer.Send(NetworkProtocol.Write(_world.Combat!.State(hit.TargetId, _world.Tick)), DeliveryMethod.ReliableOrdered);
            }
            // Resource state and unlocked slots are private, including rejection updates.
            if (_world.TryGetOwnedEntity(peer.Id, out var owner))
            {
                if (view.Entered.Contains(owner) || abilities.IsDirty(owner))
                    peer.Send(NetworkProtocol.Write(abilities.Loadout(owner, _world.Tick)), DeliveryMethod.ReliableOrdered);
                if (abilities.Results.TryGetValue(owner, out var abilityResult))
                    peer.Send(NetworkProtocol.Write(abilityResult), DeliveryMethod.ReliableOrdered);
            }
        }

        if (_world.Npc is { Telegraph: { } telegraph } npc)
        {
            if (view.Entities.Contains(npc.Id) && (view.Entered.Contains(npc.Id) || view.NpcWindupVersion != npc.WindupVersion))
                peer.Send(NetworkProtocol.Write(telegraph with { ServerTick = _world.Tick }), DeliveryMethod.ReliableOrdered);
            view.NpcWindupVersion = npc.WindupVersion;
        }
        if (_world.Boss is { } boss)
        {
            if (view.Entities.Contains(boss.Id))
            {
                if (boss.Telegraph is { } cone && (view.Entered.Contains(boss.Id) || view.BossWindupVersion != boss.WindupVersion))
                    peer.Send(NetworkProtocol.Write(cone with { ServerTick = _world.Tick }), DeliveryMethod.ReliableOrdered);
                if (boss.Area is { } area && (view.Entered.Contains(boss.Id) || view.BossAreaVersion != boss.AreaVersion))
                    peer.Send(NetworkProtocol.Write(area with { ServerTick = _world.Tick }), DeliveryMethod.ReliableOrdered);
            }
            view.BossWindupVersion = boss.WindupVersion;
            view.BossAreaVersion = boss.AreaVersion;
        }
        var chunkCapacity = NetworkProtocol.SnapshotCapacity(peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable));
        if (chunkCapacity == 0) throw new InvalidOperationException("Peer MTU cannot hold an entity snapshot.");
        for (var offset = 0; offset < view.Snapshots.Count; offset += chunkCapacity)
        {
            var count = Math.Min(chunkCapacity, view.Snapshots.Count - offset);
            NetworkProtocol.WriteWorldSnapshot(_snapshotWriter, _world.Tick, view.Snapshots, offset, count);
            // Sequenced would discard other chunks of this tick. Each entity filters its own tick.
            peer.Send(_snapshotWriter, DeliveryMethod.Unreliable);
        }
        if (_world.Combat is not { } simulation)
            return;
        // Spawn, action and health events share the reliable stream: no action before its entity.
        foreach (var action in simulation.Events)
        {
            if (view.Entities.Contains(action.AttackerId) &&
                (!action.TargetId.IsValid || view.Entities.Contains(action.TargetId)))
                peer.Send(NetworkProtocol.Write(action), DeliveryMethod.ReliableOrdered);
            else if (action.TargetId.IsValid && view.Entities.Contains(action.TargetId))
                peer.Send(NetworkProtocol.Write(simulation.State(action.TargetId, _world.Tick)), DeliveryMethod.ReliableOrdered);
        }
        // Echo damage resolves last in the tick: publish it last, so earlier attacks cannot restore stale HP.
        if (_world.Echoes is { } echoSimulation)
            foreach (var action in echoSimulation.Actions)
            {
                if (view.Entities.Contains(action.EntityId) && (!action.TargetId.IsValid || view.Entities.Contains(action.TargetId)))
                    peer.Send(NetworkProtocol.Write(action), DeliveryMethod.ReliableOrdered);
                else if (action.TargetId.IsValid && view.Entities.Contains(action.TargetId))
                    peer.Send(NetworkProtocol.Write(simulation.State(action.TargetId, _world.Tick)), DeliveryMethod.ReliableOrdered);
            }
        // Only the owner receives command rejection/acknowledgement.
        if (_world.TryGetOwnedEntity(peer.Id, out var ownedId) &&
            simulation.Results.TryGetValue(ownedId, out var result))
            peer.Send(NetworkProtocol.Write(result), DeliveryMethod.ReliableOrdered);
    }

    private void RejectMalformed(NetPeer peer, string reason) =>
        Reject(peer, HandshakeRejectCode.MalformedPacket, reason);

    private void Reject(NetPeer peer, HandshakeRejectCode code, string reason)
    {
        _logger.LogWarning(
            "Connection rejected. ConnectionId={ConnectionId}, Code={Code}, Reason={Reason}",
            peer.Id,
            code,
            reason);

        _server.DisconnectPeer(peer, NetworkProtocol.Write(new ServerReject(code, reason)));
    }

    private void OnNetworkError(
        System.Net.IPEndPoint endpoint,
        System.Net.Sockets.SocketError error)
    {
        _logger.LogWarning(
            "Network error. Endpoint={Endpoint}, SocketError={SocketError}",
            endpoint,
            error);
    }
}
