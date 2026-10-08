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
    private readonly Content.Server.WorldStory.LiveDmInbox? _liveDm;

    public GameServerService(
        IOptions<ServerOptions> options,
        HandshakeCoordinator handshakes,
        ServerWorld world,
        ILogger<GameServerService> logger,
        ICharacterStore? characters = null,
        IOptions<PersistenceOptions>? persistence = null, IHostEnvironment? environment = null, Content.Server.WorldStory.LiveDmInbox? liveDm = null)
    {
        _liveDm=liveDm;
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
        if (_world.HasWorldNode)
        {
            if (_characters is null) throw new InvalidOperationException("World node requires durable storage.");
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            _worldSession=await _characters.OpenWorldAsync(_world.WorldNodeKey,new SavedWorldNode(),deadline.Token);
            try { _world.RestoreWorldNode(_worldSession.State,_worldSession.Revision); }
            catch { await _worldSession.DisposeAsync(); _worldSession=null; throw; }
        }
        if (_liveDm?.Enabled==true && !_world.HasWorldNode) throw new InvalidOperationException("Live-DM requires a persistent world node.");
        if (!_server.Start(_options.Port))
        {
            if(_worldSession is not null) { await _worldSession.DisposeAsync(); _worldSession=null; }
            throw new InvalidOperationException($"Failed to start UDP server on port {_options.Port}.");
        }

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
                    if(_liveDm?.TryTake(out var dm)==true) _world.ApplyLiveDm(dm);
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
            if (messageType == NetworkMessageType.ProgressionCommand && deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadProgressionCommand(reader,out var progression))
            {
                if (_characters is null) _world.TryQueueProgression(peer.Id,progression);
                else
                {
                    var intentions = BufferIntentions(peer.Id);
                    if (intentions.Progression is null) intentions.Progression = progression;
                    else intentions.ProgressionSecond = progression;
                }
            }
            if (messageType == NetworkMessageType.ProfessionCommand && deliveryMethod == DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadProfessionCommand(reader,out var profession))
            {
                if (_characters is null) _world.TryQueueProfession(peer.Id,profession);
                else
                {
                    var intentions=BufferIntentions(peer.Id);
                    if (intentions.Profession is null) intentions.Profession=profession;
                    else intentions.ProfessionSecond=profession;
                }
            }
            if (messageType==NetworkMessageType.WorldNodeCommand && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadWorldNodeCommand(reader,out var node))
            {
                var intentions=BufferIntentions(peer.Id);
                if(intentions.Node is null) intentions.Node=node; else intentions.NodeSecond=node;
            }
            if (messageType==NetworkMessageType.CraftCommand && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadCraftCommand(reader,out var craft))
            { if(_characters is null) _world.TryQueueCraft(peer.Id,craft); else BufferIntentions(peer.Id).Craft ??= craft; }
            if(messageType==NetworkMessageType.EconomyCommand && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadEconomyCommand(reader,out var economy))
            { if(_characters is null) _world.TryQueueEconomy(peer.Id,economy); else BufferIntentions(peer.Id).Economy ??= economy; }
            if(messageType==NetworkMessageType.RepairCommand && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadRepairCommand(reader,out var repair))
            { if(_characters is null) _world.TryQueueRepair(peer.Id,repair); else BufferIntentions(peer.Id).Repair ??= repair; }
            if(messageType==NetworkMessageType.TradeCommand && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadTradeCommand(reader,out var trade))
            { if(_characters is null) _world.TryQueueTrade(peer.Id,trade); else BufferIntentions(peer.Id).Trade ??= trade; }
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
        if(session is not null) _world.BindWorldActor(peer.Id,session.CharacterId);
        var welcome = new ServerWelcome(
            playerId,
            checked((ushort) _options.TickRate),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session?.IssuedToken ?? "");

        peer.Send(NetworkProtocol.Write(welcome), DeliveryMethod.ReliableOrdered);
        peer.Send(NetworkProtocol.Write(new DevelopmentTools(CanDevelopmentRevive(peer))), DeliveryMethod.ReliableOrdered);
        // Public collision geometry arrives before any spawn on the same reliable stream.
        peer.Send(NetworkProtocol.Write(_world.Navigation.ToMessage()), DeliveryMethod.ReliableOrdered);

        if (_world.HasStarterZone) peer.Send(NetworkProtocol.Write(_world.PublicStarterZone()),DeliveryMethod.ReliableOrdered);
        if (_world.HasCrafting) foreach(var recipe in _world.PublicCraftRecipes()) peer.Send(NetworkProtocol.Write(recipe),DeliveryMethod.ReliableOrdered);
        var view = new InterestView { BridgeNavigationSent=_world.HasWorldNode && (_world.PublicWorldNode().Consequences&1)!=0 };
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
        _world.ClearCraftResults();
        _world.ClearRepairResults();
        _world.ClearTradeResults();
        _world.ClearEconomyResults();
        _world.ClearExplorationResults();
        _world.ClearProgressionResults();
        _world.ClearProfessionResults();
        _world.ClearWorldNodeResults();
        if (_characters is null) _world.GroundItems?.CommitClaims();
    }

    private void SendInterest(NetPeer peer, InterestView view)
    {
        if(_world.HasWorldNode)
        {
            var state=_world.PublicWorldNode();
            if((state.Consequences&1)!=0 && !view.BridgeNavigationSent)
            { peer.Send(NetworkProtocol.Write(_world.Navigation.ToMessage()),DeliveryMethod.ReliableOrdered); view.BridgeNavigationSent=true; }
            if(view.WorldNodeRevision!=state.Revision)
            { peer.Send(NetworkProtocol.Write(state),DeliveryMethod.ReliableOrdered); view.WorldNodeRevision=state.Revision; }
            if(_world.TryGetOwnedEntity(peer.Id,out var ownerId) && _world.WorldNodeResults.TryGetValue(ownerId,out var nodeResult)) peer.Send(NetworkProtocol.Write(nodeResult),DeliveryMethod.ReliableOrdered);
        }
        _world.UpdateInterest(peer.Id, view);
        _world.UpdateGroundInterest(peer.Id, view);
        _world.UpdateResourceInterest(peer.Id,view);
        foreach(var id in view.ResourceLeft) peer.Send(NetworkProtocol.Write(new ResourceNodeDespawn(id,_world.Tick)),DeliveryMethod.ReliableOrdered);
        foreach(var id in view.ResourceVisible) if(view.ResourceEntered.Contains(id) || _world.IsResourceDirty(id)) peer.Send(NetworkProtocol.Write(_world.ResourceState(id)),DeliveryMethod.ReliableOrdered);
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
        if(_world.HasEconomy && _world.TryGetOwnedEntity(peer.Id,out var economyOwner))
        {
            if(view.Entered.Contains(economyOwner) || _world.Inventory!.IsDirty(economyOwner) || _world.EconomyResults.ContainsKey(economyOwner)) peer.Send(NetworkProtocol.Write(_world.Inventory!.EconomyState(economyOwner,_world.Tick)),DeliveryMethod.ReliableOrdered);
            var marketVisible=_world.CanViewMarket(peer.Id);
            if(view.Entered.Contains(economyOwner) || marketVisible!=view.MarketVisible || _world.MarketDirty || _world.EconomyResults.ContainsKey(economyOwner))
            {
                var market=_world.MarketState(peer.Id,marketVisible);
                if(marketVisible || marketVisible!=view.MarketVisible || market.Credit!=view.MarketCreditSent || view.Entered.Contains(economyOwner))peer.Send(NetworkProtocol.Write(market),DeliveryMethod.ReliableOrdered);
                view.MarketCreditSent=market.Credit;
            }
            view.MarketVisible=marketVisible;
            if(_world.EconomyQuotes.TryGetValue(economyOwner,out var economyQuote))peer.Send(NetworkProtocol.Write(economyQuote),DeliveryMethod.ReliableOrdered);
            if(_world.EconomyResults.TryGetValue(economyOwner,out var economyResult))peer.Send(NetworkProtocol.Write(economyResult),DeliveryMethod.ReliableOrdered);
        }
        if(_world.TryGetOwnedEntity(peer.Id,out var tradeOwner))
        { if(_world.TradeStates.TryGetValue(tradeOwner,out var state)) peer.Send(NetworkProtocol.Write(state),DeliveryMethod.ReliableOrdered); if(_world.TradeResults.TryGetValue(tradeOwner,out var tradeResult)) peer.Send(NetworkProtocol.Write(tradeResult),DeliveryMethod.ReliableOrdered); }
        if(_world.Inventory is { } conditionInventory && _world.TryGetOwnedEntity(peer.Id,out var conditionOwner))
        {
            if(view.Entered.Contains(conditionOwner) || conditionInventory.IsDirty(conditionOwner) || _world.RepairResults.ContainsKey(conditionOwner)) peer.Send(NetworkProtocol.Write(conditionInventory.ConditionState(conditionOwner,_world.Tick)),DeliveryMethod.ReliableOrdered);
            if(_world.RepairQuotes.TryGetValue(conditionOwner,out var quote)) peer.Send(NetworkProtocol.Write(quote),DeliveryMethod.ReliableOrdered);
            if(_world.RepairResults.TryGetValue(conditionOwner,out var repairResult)) peer.Send(NetworkProtocol.Write(repairResult),DeliveryMethod.ReliableOrdered);
        }
        if (_world.HasCrafting && _world.TryGetOwnedEntity(peer.Id,out var craftOwner))
        {
            if(view.Entered.Contains(craftOwner) || _world.Inventory!.IsDirty(craftOwner) || _world.CraftResults.ContainsKey(craftOwner)) peer.Send(NetworkProtocol.Write(_world.CraftState(craftOwner)),DeliveryMethod.ReliableOrdered);
            if(_world.CraftResults.TryGetValue(craftOwner,out var craftResult)) peer.Send(NetworkProtocol.Write(craftResult),DeliveryMethod.ReliableOrdered);
        }
        if (_world.HasStarterZone && _world.TryGetOwnedEntity(peer.Id,out var explorationOwner) &&
            (view.Entered.Contains(explorationOwner) || _world.IsExplorationDirty(explorationOwner)))
            peer.Send(NetworkProtocol.Write(_world.ExplorationState(explorationOwner)),DeliveryMethod.ReliableOrdered);
        if (_world.HasProgression && _world.TryGetOwnedEntity(peer.Id,out var progressionOwner))
        {
            if (view.Entered.Contains(progressionOwner) || _world.IsProgressionDirty(progressionOwner))
                peer.Send(NetworkProtocol.Write(_world.ProgressionState(progressionOwner,_world.Tick)),DeliveryMethod.ReliableOrdered);
            if (_world.ProgressionResults.TryGetValue(progressionOwner,out var progressionResult))
                peer.Send(NetworkProtocol.Write(progressionResult),DeliveryMethod.ReliableOrdered);
        }
        if (_world.HasProgression && _world.TryGetOwnedEntity(peer.Id,out var professionOwner))
        {
            if (view.Entered.Contains(professionOwner) || _world.IsProfessionDirty(professionOwner))
                peer.Send(NetworkProtocol.Write(_world.ProfessionState(professionOwner,_world.Tick)),DeliveryMethod.ReliableOrdered);
            if (_world.ProfessionResults.TryGetValue(professionOwner,out var professionResult))
                peer.Send(NetworkProtocol.Write(professionResult),DeliveryMethod.ReliableOrdered);
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
