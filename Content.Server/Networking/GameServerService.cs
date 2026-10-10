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
    private readonly SocialOptions _socialOptions;
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
        IOptions<PersistenceOptions>? persistence = null, IHostEnvironment? environment = null, Content.Server.WorldStory.LiveDmInbox? liveDm = null, IOptions<SocialOptions>? social = null, Content.Server.Regions.RegionalWorlds? regions = null)
    {
        if (regions is not null && !ReferenceEquals(regions.Primary, world))
            throw new ArgumentException("The primary world must belong to the regional configuration.", nameof(world));
        _regionalWorlds = regions;
        _worlds = regions?.Worlds ?? new[] { world };
        _liveDm=liveDm;
        _socialOptions=social?.Value??new();if(!_socialOptions.IsValid())throw new ArgumentException("Invalid social budgets.");
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
        try
        {
            await InitializeWorldsAsync(stoppingToken);
            if (!_server.Start(_options.Port))
                throw new InvalidOperationException($"Failed to start UDP server on port {_options.Port}.");
        }
        catch
        {
            await DisposeWorldLeasesAsync();
            throw;
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
                CompleteTravel();
                if (!WaitingForDurability)
                {
                    ProcessDisconnectedPlayers();
                    CompleteLogins();
                    CompleteClosings();
                    CloseDepartedPlayers();
                }

                // Limit catch-up work so a temporary stall cannot spiral indefinitely.
                for (var catchUp = 0; !WaitingForDurability && clock.Elapsed >= nextTick && catchUp < 4; catchUp++)
                {
                    ApplyBufferedIntentions();
                    if(_liveDm?.TryTake(out var dm)==true) _world.ApplyLiveDm(dm);
                    if (_regions is null) _world.Simulate(fixedDelta);
                    else _regions.Simulate(fixedDelta);
                    if (!BeginTravel() && !BeginCheckpoint()) BroadcastSnapshot();
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
        _disconnected.Add(peer.Id);
        socialSent.Remove(peer.Id); socialPresenceSent.Remove(peer.Id);


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

            if (_regions is not null)
            {
                if (messageType != NetworkMessageType.RegionPacket ||
                    !NetworkProtocol.TryReadRegionHeader(reader, out var epoch, out messageType))
                { RejectMalformed(peer, "Expected a bounded regional command."); return; }
                var owner = _regions.Owner(peer.Id);
                if (epoch != owner.Epoch || !_regions.CanExecute(peer.Id, owner)) return;
            }
            var world = WorldFor(peer.Id);
            if (messageType is NetworkMessageType.RegionReady or NetworkMessageType.RegionApplied)
            { HandleRegionLoad(peer, reader, messageType, deliveryMethod); return; }
            if (!world.GetPlayer(peer.Id).Loaded) return;

            // NC: ownership comes from the connection; the packet cannot choose another avatar.
            if (messageType == NetworkMessageType.EmoteCommand && deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadEmoteCommand(reader, out var emote))
            {
                if (_characters is null) world.TryQueueEmote(peer.Id, emote);
                else BufferIntentions(peer.Id).Emote ??= emote;
                return;
            }

            if(messageType==NetworkMessageType.QuestCommand && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadQuestCommand(reader,out var quest))
            {
                if(_characters is null) world.TryQueueQuest(peer.Id,quest);
                else BufferIntentions(peer.Id).Quest ??= quest;
                return;
            }

            if (messageType == NetworkMessageType.MoveCommand &&
                NetworkProtocol.TryReadMoveCommand(reader, out var command))
            {
                if (_characters is null) world.TryApplyMove(peer.Id, command);
                else BufferIntentions(peer.Id).Move = command;
                return;
            }
            if (messageType == NetworkMessageType.DefenseCommand &&
                NetworkProtocol.TryReadDefenseCommand(reader,out var defense) &&
                (deliveryMethod==DeliveryMethod.ReliableOrdered || defense.Action==DefenseAction.Block && deliveryMethod==DeliveryMethod.Unreliable))
            {
                if (_characters is null) world.TryQueueDefense(peer.Id,defense);
                else
                {
                    var buffered=BufferIntentions(peer.Id);
                    // Keep a deliberate parry over subsequent facing heartbeats during an in-flight save.
                    if (buffered.Defense is not { Action: DefenseAction.Parry } || defense.Action==DefenseAction.Release)
                        buffered.Defense=defense;
                }
            }
            if (messageType == NetworkMessageType.AttackCommand &&
                deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadAttackCommand(reader, out var attack))
            {
                if (_characters is null) world.TryQueueAttack(peer.Id, attack);
                else BufferIntentions(peer.Id).Attack ??= attack;
            }
            if (messageType == NetworkMessageType.AbilityCommand &&
                deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadAbilityCommand(reader, out var ability))
            {
                if (_characters is null) world.TryQueueAbility(peer.Id, ability, peer.Ping);
                else BufferIntentions(peer.Id).Ability ??= ability;
            }
            if (messageType == NetworkMessageType.InventoryCommand && deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadInventoryCommand(reader, out var inventory))
            {
                if (_characters is null) world.TryQueueInventory(peer.Id, inventory);
                else BufferIntentions(peer.Id).Inventory ??= inventory;
            }
            if (messageType == NetworkMessageType.PickupCommand && deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadPickupCommand(reader, out var pickup))
            {
                if (_characters is null) world.TryQueuePickup(peer.Id, pickup);
                else BufferIntentions(peer.Id).Pickup ??= pickup;
            }
            if (messageType == NetworkMessageType.DevelopmentRevive && CanDevelopmentRevive(peer) &&
                deliveryMethod == DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadDevelopmentRevive(reader, out var revive))
            {
                if (_characters is null) world.TryQueueDevelopmentRevive(peer.Id, revive, authorized: true);
                else BufferIntentions(peer.Id).Revive ??= revive;
            }
            if (messageType == NetworkMessageType.EchoSignatureCommand && deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadEchoSignatureCommand(reader,out var echo))
            {
                if (_characters is null) world.TryQueueEchoSignature(peer.Id,echo);
                else BufferIntentions(peer.Id).Echo ??= echo;
            }
            if (messageType == NetworkMessageType.ProgressionCommand && deliveryMethod == DeliveryMethod.ReliableOrdered &&
                NetworkProtocol.TryReadProgressionCommand(reader,out var progression))
            {
                if (_characters is null) world.TryQueueProgression(peer.Id,progression);
                else
                {
                    var intentions = BufferIntentions(peer.Id);
                    if (intentions.Progression is null) intentions.Progression = progression;
                    else intentions.ProgressionSecond = progression;
                }
            }
            if (messageType == NetworkMessageType.ProfessionCommand && deliveryMethod == DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadProfessionCommand(reader,out var profession))
            {
                if (_characters is null) world.TryQueueProfession(peer.Id,profession);
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
            { if(_characters is null) world.TryQueueCraft(peer.Id,craft); else BufferIntentions(peer.Id).Craft ??= craft; }
            if(messageType==NetworkMessageType.EconomyCommand && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadEconomyCommand(reader,out var economy))
            { if(_characters is null) world.TryQueueEconomy(peer.Id,economy); else BufferIntentions(peer.Id).Economy ??= economy; }
            if(messageType==NetworkMessageType.RepairCommand && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadRepairCommand(reader,out var repair))
            { if(_characters is null) world.TryQueueRepair(peer.Id,repair); else BufferIntentions(peer.Id).Repair ??= repair; }
            if(messageType==NetworkMessageType.TradeCommand && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadTradeCommand(reader,out var trade))
            { if(_characters is null) world.TryQueueTrade(peer.Id,trade); else BufferIntentions(peer.Id).Trade ??= trade; }
            if(messageType==NetworkMessageType.PvpCommand&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadPvpCommand(reader,out var pvp))
            {if(_characters is null)world.TryQueuePvp(peer.Id,pvp);else BufferIntentions(peer.Id).Pvp??=pvp;}
            if(messageType==NetworkMessageType.SocialCommand&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadSocialCommand(reader,out var social))
                BufferIntentions(peer.Id).Social??=social;
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

    private void AcceptPlayer(NetPeer peer, ClientHello hello, PlayerId playerId, CharacterSession? session = null, bool reattached=false)
    {
        var player = reattached ? WorldFor(peer.Id).GetPlayer(peer.Id) :
            _regions is not null ? _regions.AddPlayer(peer.Id, playerId, session!) : this._world.AddPlayer(peer.Id, playerId, session?.State);
        var world = WorldFor(peer.Id);
        if(session is not null && (_regions is null || reattached))
        {
            try { world.BindWorldActor(peer.Id,session.CharacterId); world.BindSocial(peer.Id,session.CharacterId); }
            catch { if(!reattached)RemoveRegionalPlayer(peer.Id); throw; }
        }
        var welcome = new ServerWelcome(
            playerId,
            checked((ushort) _options.TickRate),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session?.IssuedToken ?? "");

        peer.Send(NetworkProtocol.Write(welcome), DeliveryMethod.ReliableOrdered);
        SendRegionBootstrap(peer);

        _logger.LogInformation(
            "Handshake accepted. ConnectionId={ConnectionId}, PlayerId={PlayerId}, EntityId={EntityId}, Build={BuildVersion}",
            peer.Id,
            playerId.Value,
            player.EntityId.Value,
            hello.BuildVersion);
    }

    private void BroadcastSnapshot()
    {
        foreach (var (connectionId, peer) in _peers)
        {
            if (_views.TryGetValue(connectionId, out var view))
                SendInterest(peer, view);
        }
        foreach (var world in _worlds)
        {
            world.Combat?.ClearResults();
            world.ClearQuestResults();
            world.Abilities?.ClearResults();
            world.Inventory?.ClearResults();
            world.GroundItems?.ClearResults();
            world.Echoes?.ClearResults();
            world.ClearCraftResults();
            world.ClearRepairResults();
            world.ClearTradeResults();
            world.ClearEconomyResults();
            world.ClearPvpResults();
            world.SocialResults.Clear();
            world.ClearExplorationResults();
            world.ClearProgressionResults();
            world.ClearProfessionResults();
            world.ClearWorldNodeResults();
            if (_characters is null) world.GroundItems?.CommitClaims();
        }
        _world.Social?.Changed.Clear();
    }

    private void SendInterest(NetPeer peer, InterestView view)
    {
        if (_loading.TryGetValue(peer.Id, out var load))
        {
            if (Environment.TickCount64 > load.Deadline) { peer.Disconnect(); return; }
            if (load.Stage == 0) return;
        }
        var world = WorldFor(peer.Id);
        if(world.HasWorldEvent)
        {
            var state=world.PublicWorldNode();
            if((state.Consequences&1)!=0 && !view.BridgeNavigationSent)
            { SendGame(peer, NetworkProtocol.Write(world.Navigation.ToMessage()),DeliveryMethod.ReliableOrdered); view.BridgeNavigationSent=true; }
            if(view.WorldNodeRevision!=state.Revision)
            { SendGame(peer, NetworkProtocol.Write(state),DeliveryMethod.ReliableOrdered); view.WorldNodeRevision=state.Revision; }
            if(world.TryGetOwnedEntity(peer.Id,out var ownerId) && world.WorldNodeResults.TryGetValue(ownerId,out var nodeResult)) SendGame(peer, NetworkProtocol.Write(nodeResult),DeliveryMethod.ReliableOrdered);
        }
        world.UpdateInterest(peer.Id, view);
        world.UpdateGroundInterest(peer.Id, view);
        world.UpdateResourceInterest(peer.Id,view);
        foreach(var id in view.ResourceLeft) SendGame(peer, NetworkProtocol.Write(new ResourceNodeDespawn(id,world.Tick)),DeliveryMethod.ReliableOrdered);
        foreach(var id in view.ResourceVisible) if(view.ResourceEntered.Contains(id) || world.IsResourceDirty(id)) SendGame(peer, NetworkProtocol.Write(world.ResourceState(id)),DeliveryMethod.ReliableOrdered);
        if (world.GroundItems is { } ground)
        {
            foreach (var id in view.GroundLeft)
                SendGame(peer, NetworkProtocol.Write(new GroundItemDespawn(id.Value, world.Tick)), DeliveryMethod.ReliableOrdered);
            foreach (var id in view.GroundEntered)
            {SendGame(peer, NetworkProtocol.Write(ground.State(id.Value, world.Tick)), DeliveryMethod.ReliableOrdered);if(ground.IsDeathLoot(id.Value))SendGame(peer, NetworkProtocol.Write(ground.LootState(id.Value)),DeliveryMethod.ReliableOrdered);}
        }
        foreach (var id in view.Left)
            SendGame(peer, NetworkProtocol.Write(new PlayerDespawn(id)), DeliveryMethod.ReliableOrdered);
        foreach (var id in view.Entered)
        {
            if(world.TryGetQuestNpc(id,out var questNpc))
            { SendGame(peer,NetworkProtocol.Write(questNpc),DeliveryMethod.ReliableOrdered); continue; }
            if (world.Echoes?.TryGet(id,out _) == true)
            {
                SendGame(peer, NetworkProtocol.Write(world.Echoes.Spawn(id,world.Tick)),DeliveryMethod.ReliableOrdered);
                continue;
            }
            if (world.IsPlayer(id))
                SendGame(peer, NetworkProtocol.Write(world.CreateSpawn(id)), DeliveryMethod.ReliableOrdered);
            if (world.Combat is { } combat)
                SendGame(peer, NetworkProtocol.Write(combat.State(id, world.Tick)), DeliveryMethod.ReliableOrdered);
        }

        if (world.Combat is { } equipmentCombat)
            foreach (var id in equipmentCombat.EquipmentDirty)
                if (view.Entities.Contains(id) && !view.Entered.Contains(id))
                    SendGame(peer, NetworkProtocol.Write(equipmentCombat.State(id, world.Tick)), DeliveryMethod.ReliableOrdered);
        if(world.HasPvp)
        {
            foreach(var id in view.Entities)if(world.IsPlayer(id))
            {
                var flags=world.PublicPvp(id);
                if(!view.PvpFlags.TryGetValue(id,out var previous)||previous with {ServerTick=flags.ServerTick}!=flags){SendGame(peer, NetworkProtocol.Write(flags),DeliveryMethod.ReliableOrdered);view.PvpFlags[id]=flags;}
            }
            foreach(var id in view.Left)view.PvpFlags.Remove(id);
            if(world.TryGetOwnedEntity(peer.Id,out var owner))
            {
                if(view.Entered.Contains(owner)||world.IsPvpDirty(owner))SendGame(peer, NetworkProtocol.Write(world.PrivatePvp(owner)),DeliveryMethod.ReliableOrdered);
                if(world.PvpResults.TryGetValue(owner,out var pvpResult))SendGame(peer, NetworkProtocol.Write(pvpResult),DeliveryMethod.ReliableOrdered);
                if(view.Entered.Contains(owner)||world.GroundItems!.ChannelDirty(owner))SendGame(peer, NetworkProtocol.Write(world.GroundItems!.ChannelState(owner,world.Tick)),DeliveryMethod.ReliableOrdered);
            }
        }
        SendSocial(peer);
        if(world.HasQuests && world.TryGetOwnedEntity(peer.Id,out var questOwner))
        {
            if(view.Entered.Contains(questOwner) || world.IsProgressionDirty(questOwner) || world.Inventory!.IsDirty(questOwner) || world.QuestReplies.ContainsKey(questOwner))
                SendGame(peer,NetworkProtocol.Write(world.QuestJournal(questOwner)),DeliveryMethod.ReliableOrdered);
            if(world.QuestReplies.TryGetValue(questOwner,out var questReply))
                SendGame(peer,NetworkProtocol.Write(questReply),DeliveryMethod.ReliableOrdered);
        }
        if (world.Inventory is { } inventory && world.TryGetOwnedEntity(peer.Id, out var inventoryOwner))
        {
            if (view.Entered.Contains(inventoryOwner) || inventory.IsDirty(inventoryOwner))
                SendGame(peer, NetworkProtocol.Write(inventory.State(inventoryOwner, world.Tick)), DeliveryMethod.ReliableOrdered);
            if (inventory.Results.TryGetValue(inventoryOwner, out var inventoryResult))
                SendGame(peer, NetworkProtocol.Write(inventoryResult), DeliveryMethod.ReliableOrdered);
        }
        if (world.GroundItems is { } pickups && world.TryGetOwnedEntity(peer.Id, out var pickupOwner) &&
            pickups.Results.TryGetValue(pickupOwner, out var pickupResult))
            SendGame(peer, NetworkProtocol.Write(pickupResult), DeliveryMethod.ReliableOrdered);
        if (world.Echoes is { } echoes)
        {
            if (world.TryGetOwnedEntity(peer.Id,out var owner))
            {
                if (view.Entered.Contains(owner) || echoes.IsLoadoutDirty(owner))
                    SendGame(peer, NetworkProtocol.Write(echoes.Loadout(owner,world.Tick)),DeliveryMethod.ReliableOrdered);
                if (echoes.Results.TryGetValue(owner,out var echoResult))
                    SendGame(peer, NetworkProtocol.Write(echoResult),DeliveryMethod.ReliableOrdered);
            }
        }
        if(world.HasEconomy && world.TryGetOwnedEntity(peer.Id,out var economyOwner))
        {
            if(view.Entered.Contains(economyOwner) || world.Inventory!.IsDirty(economyOwner) || world.EconomyResults.ContainsKey(economyOwner)) SendGame(peer, NetworkProtocol.Write(world.Inventory!.EconomyState(economyOwner,world.Tick)),DeliveryMethod.ReliableOrdered);
            var marketVisible=world.CanViewMarket(peer.Id);
            if(view.Entered.Contains(economyOwner) || marketVisible!=view.MarketVisible || world.MarketDirty || world.EconomyResults.ContainsKey(economyOwner))
            {
                var market=world.MarketState(peer.Id,marketVisible);
                if(marketVisible || marketVisible!=view.MarketVisible || market.Credit!=view.MarketCreditSent || view.Entered.Contains(economyOwner))SendGame(peer, NetworkProtocol.Write(market),DeliveryMethod.ReliableOrdered);
                view.MarketCreditSent=market.Credit;
            }
            view.MarketVisible=marketVisible;
            if(world.EconomyQuotes.TryGetValue(economyOwner,out var economyQuote))SendGame(peer, NetworkProtocol.Write(economyQuote),DeliveryMethod.ReliableOrdered);
            if(world.EconomyResults.TryGetValue(economyOwner,out var economyResult))SendGame(peer, NetworkProtocol.Write(economyResult),DeliveryMethod.ReliableOrdered);
        }
        if(world.TryGetOwnedEntity(peer.Id,out var tradeOwner))
        { if(world.TradeStates.TryGetValue(tradeOwner,out var state)) SendGame(peer, NetworkProtocol.Write(state),DeliveryMethod.ReliableOrdered); if(world.TradeResults.TryGetValue(tradeOwner,out var tradeResult)) SendGame(peer, NetworkProtocol.Write(tradeResult),DeliveryMethod.ReliableOrdered); }
        if(world.Inventory is { } conditionInventory && world.TryGetOwnedEntity(peer.Id,out var conditionOwner))
        {
            if(view.Entered.Contains(conditionOwner) || conditionInventory.IsDirty(conditionOwner) || world.RepairResults.ContainsKey(conditionOwner)) SendGame(peer, NetworkProtocol.Write(conditionInventory.ConditionState(conditionOwner,world.Tick)),DeliveryMethod.ReliableOrdered);
            if(world.RepairQuotes.TryGetValue(conditionOwner,out var quote)) SendGame(peer, NetworkProtocol.Write(quote),DeliveryMethod.ReliableOrdered);
            if(world.RepairResults.TryGetValue(conditionOwner,out var repairResult)) SendGame(peer, NetworkProtocol.Write(repairResult),DeliveryMethod.ReliableOrdered);
        }
        if (world.HasCrafting && world.TryGetOwnedEntity(peer.Id,out var craftOwner))
        {
            if(view.Entered.Contains(craftOwner) || world.Inventory!.IsDirty(craftOwner) || world.CraftResults.ContainsKey(craftOwner)) SendGame(peer, NetworkProtocol.Write(world.CraftState(craftOwner)),DeliveryMethod.ReliableOrdered);
            if(world.CraftResults.TryGetValue(craftOwner,out var craftResult)) SendGame(peer, NetworkProtocol.Write(craftResult),DeliveryMethod.ReliableOrdered);
        }
        if (world.HasStarterZone && world.TryGetOwnedEntity(peer.Id,out var explorationOwner) &&
            (view.Entered.Contains(explorationOwner) || world.IsExplorationDirty(explorationOwner)))
            SendGame(peer, NetworkProtocol.Write(world.ExplorationState(explorationOwner)),DeliveryMethod.ReliableOrdered);
        if (world.HasProgression && world.TryGetOwnedEntity(peer.Id,out var progressionOwner))
        {
            if (world.Combat is { } defenses && (view.Entered.Contains(progressionOwner) || world.Tick%5==0 || defenses.DefenseDirty.Contains(progressionOwner)))
                SendGame(peer,NetworkProtocol.Write(defenses.DefenseState(progressionOwner,world.Tick)),DeliveryMethod.Unreliable);
            if (view.Entered.Contains(progressionOwner) || world.IsProgressionDirty(progressionOwner))
                SendGame(peer, NetworkProtocol.Write(world.ProgressionState(progressionOwner,world.Tick)),DeliveryMethod.ReliableOrdered);
            if (world.ProgressionResults.TryGetValue(progressionOwner,out var progressionResult))
                SendGame(peer, NetworkProtocol.Write(progressionResult),DeliveryMethod.ReliableOrdered);
            if (world.StatPreviews.TryGetValue(progressionOwner,out var statPreview))
                SendGame(peer, NetworkProtocol.Write(statPreview),DeliveryMethod.ReliableOrdered);
        }
        if (world.HasProgression && world.TryGetOwnedEntity(peer.Id,out var professionOwner))
        {
            if (view.Entered.Contains(professionOwner) || world.IsProfessionDirty(professionOwner))
                SendGame(peer, NetworkProtocol.Write(world.ProfessionState(professionOwner,world.Tick)),DeliveryMethod.ReliableOrdered);
            if (world.ProfessionResults.TryGetValue(professionOwner,out var professionResult))
                SendGame(peer, NetworkProtocol.Write(professionResult),DeliveryMethod.ReliableOrdered);
        }
        if (world.Abilities is { } abilities)
        {
            world.UpdateAbilityInterest(peer.Id, view, view.Abilities);
            foreach (var effect in view.Abilities.Changes)
                SendGame(peer, NetworkProtocol.Write(effect), DeliveryMethod.ReliableOrdered);
            foreach (var hit in abilities.Hits)
            {
                if (view.Entities.Contains(hit.ActorId) && view.Entities.Contains(hit.TargetId))
                    SendGame(peer, NetworkProtocol.Write(hit), DeliveryMethod.ReliableOrdered);
                else if (view.Entities.Contains(hit.TargetId))
                    SendGame(peer, NetworkProtocol.Write(world.Combat!.State(hit.TargetId, world.Tick)), DeliveryMethod.ReliableOrdered);
            }
            // Resource state and unlocked slots are private, including rejection updates.
            if (world.TryGetOwnedEntity(peer.Id, out var owner))
            {
                if (view.Entered.Contains(owner) || abilities.IsDirty(owner))
                    SendGame(peer, NetworkProtocol.Write(abilities.Loadout(owner, world.Tick)), DeliveryMethod.ReliableOrdered);
                if (abilities.Results.TryGetValue(owner, out var abilityResult))
                    SendGame(peer, NetworkProtocol.Write(abilityResult), DeliveryMethod.ReliableOrdered);
            }
        }

        if (world.Npc is { Telegraph: { } telegraph } npc)
        {
            if (view.Entities.Contains(npc.Id) && (view.Entered.Contains(npc.Id) || view.NpcWindupVersion != npc.WindupVersion))
                SendGame(peer, NetworkProtocol.Write(telegraph with { ServerTick = world.Tick }), DeliveryMethod.ReliableOrdered);
            view.NpcWindupVersion = npc.WindupVersion;
        }
        if (world.Boss is { } boss)
        {
            if (view.Entities.Contains(boss.Id))
            {
                if (boss.Telegraph is { } cone && (view.Entered.Contains(boss.Id) || view.BossWindupVersion != boss.WindupVersion))
                    SendGame(peer, NetworkProtocol.Write(cone with { ServerTick = world.Tick }), DeliveryMethod.ReliableOrdered);
                if (boss.Area is { } area && (view.Entered.Contains(boss.Id) || view.BossAreaVersion != boss.AreaVersion))
                    SendGame(peer, NetworkProtocol.Write(area with { ServerTick = world.Tick }), DeliveryMethod.ReliableOrdered);
            }
            view.BossWindupVersion = boss.WindupVersion;
            view.BossAreaVersion = boss.AreaVersion;
        }
        var chunkCapacity = NetworkProtocol.SnapshotCapacity(Math.Min(NetworkConstants.MaxGamePacketBytes, peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable)) - (_regions is null ? 0 : NetworkProtocol.RegionEnvelopeBytes));
        // NC: public pose snapshots repair packet loss, and are limited to this peer's AOI.
        if (world.Combat is not null)
            foreach (var id in view.Entities)
                if (world.IsPlayer(id) && (view.Entered.Contains(id) || world.Tick % 2 == 0))
                    SendGame(peer, NetworkProtocol.Write(world.Avatar(id)), DeliveryMethod.Unreliable);
        if (chunkCapacity == 0) throw new InvalidOperationException("Peer MTU cannot hold an entity snapshot.");
        for (var offset = 0; offset < view.Snapshots.Count; offset += chunkCapacity)
        {
            var count = Math.Min(chunkCapacity, view.Snapshots.Count - offset);
            NetworkProtocol.WriteWorldSnapshot(_snapshotWriter, world.Tick, view.Snapshots, offset, count);
            // Sequenced would discard other chunks of this tick. Each entity filters its own tick.
            SendGame(peer, _snapshotWriter, DeliveryMethod.Unreliable);
        }
        if (world.Combat is not { } simulation)
            return;
        // Spawn, action and health events share the reliable stream: no action before its entity.
        foreach (var action in simulation.Events)
        {
            if (view.Entities.Contains(action.AttackerId) &&
                (!action.TargetId.IsValid || view.Entities.Contains(action.TargetId)))
                SendGame(peer, NetworkProtocol.Write(action), DeliveryMethod.ReliableOrdered);
            else if (action.TargetId.IsValid && view.Entities.Contains(action.TargetId))
                SendGame(peer, NetworkProtocol.Write(simulation.State(action.TargetId, world.Tick)), DeliveryMethod.ReliableOrdered);
        }
        // Echo damage resolves last in the tick: publish it last, so earlier attacks cannot restore stale HP.
        if (world.Echoes is { } echoSimulation)
            foreach (var action in echoSimulation.Actions)
            {
                if (view.Entities.Contains(action.EntityId) && (!action.TargetId.IsValid || view.Entities.Contains(action.TargetId)))
                    SendGame(peer, NetworkProtocol.Write(action), DeliveryMethod.ReliableOrdered);
                else if (action.TargetId.IsValid && view.Entities.Contains(action.TargetId))
                    SendGame(peer, NetworkProtocol.Write(simulation.State(action.TargetId, world.Tick)), DeliveryMethod.ReliableOrdered);
            }
        // Only the owner receives command rejection/acknowledgement.
        if (world.TryGetOwnedEntity(peer.Id, out var ownedId) &&
            simulation.Results.TryGetValue(ownedId, out var result))
            SendGame(peer, NetworkProtocol.Write(result), DeliveryMethod.ReliableOrdered);
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
