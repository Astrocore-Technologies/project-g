using System.Reflection;
using Content.Shared.Movement;
using Content.Shared.Network;
using Content.Shared.Navigation;
using Godot;
using LiteNetLib;
using LiteNetLib.Utils;

namespace ProjectG.Networking;

public partial class NetworkClient : Node
{
    private uint _inputOrder;
    internal event Action<MoveCommand>? MovementSent;
    public event Action<FollowState>? FollowReceived;
    public void SendFollow(FollowCommand command)
    { if (_handshakeComplete && RegionActive) SendGame(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered); }
    internal uint NextInputOrder()
    {
        if (++_inputOrder == 0) ++_inputOrder;
        return _inputOrder;
    }
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
    public event Action<BasicActionState>? BasicActionReceived;
    public event Action<BasicInputState>? BasicInputReceived;
    public BasicInputState? LatestBasicInput {get;private set;}
    private readonly BasicActionStream _basicActions=new();
    public event Action<AbilityLoadout>? AbilityLoadoutReceived;
    public event Action<AbilityResult>? AbilityResultReceived;
    public event Action<AbilityEffectState>? AbilityEffectReceived;
    private readonly AbilityEffectStream _sustainedEffects=new();
    public event Action<AbilityHit>? AbilityHitReceived;
    public event Action<CombatFeedback>? CombatFeedbackReceived;
    public event Action<ControlFeedback>? ControlFeedbackReceived;
    public event Action<CombatJournalIndex>? CombatJournalIndexReceived;
    public event Action<CombatJournalChunk>? CombatJournalChunkReceived;
    public event Action<CombatJournalResult>? CombatJournalResultReceived;
    public CombatJournalIndex? LatestCombatJournalIndex {get;private set;}
    public event Action<NpcWindup>? NpcWindupReceived;
    public event Action<NpcArea>? NpcAreaReceived;
    public event Action<InventoryState>? InventoryReceived;
    public event Action<InventoryResult>? InventoryResultReceived;
    public event Action<GroundItemSpawn>? GroundItemSpawned;
    public event Action<GroundItemDespawn>? GroundItemDespawned;
    public event Action<PickupResult>? PickupResultReceived;
    public event Action? Disconnected;
    public event Action<LogoutResult>? LogoutReceived;
    public event Action<ActorLifeState>? LifeReceived;
    public event Action<RespawnResult>? RespawnReceived;
    public Dictionary<NetworkEntityId,ActorLifeState> KnownLife {get;}=new();
    public void SendRespawn(RespawnCommand command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}
    public bool LoggedOut {get;private set;}
    public bool LogoutPending {get;private set;}
    private uint _logoutSequence;
    public bool RequestLogout()
    {
        if(!_handshakeComplete || LogoutPending || !RegionActive && CurrentRegion is not null)return false;
        LogoutPending=true;
        SendGame(NetworkProtocol.Write(new LogoutCommand(++_logoutSequence)),DeliveryMethod.ReliableOrdered);
        return true;
    }
    // NC: player-facing errors never display credentials or raw server diagnostics.
    public string LastConnectionError { get; private set; } = "";
    public event Action<string>? ConnectionFailed;
    public Dictionary<ushort,CraftRecipeState> CraftRecipes { get; }=new();
    public Dictionary<ushort,ResourceNodeState> ResourceNodes { get; }=new();
    public event Action<CraftState>? CraftStateReceived;
    public event Action<ItemConditionState>? ItemConditionReceived;
    public event Action<RepairQuote>? RepairQuoteReceived;
    public event Action<RepairResult>? RepairResultReceived;
    public event Action<EconomyState>? EconomyStateReceived;
    public event Action<EconomyQuote>? EconomyQuoteReceived;
    public event Action<EconomyResult>? EconomyResultReceived;
    public event Action<MarketState>? MarketStateReceived;
    public event Action<SocialRoster>? SocialRosterReceived;
    public event Action<SocialInvites>? SocialInvitesReceived;
    public event Action<SocialResult>? SocialResultReceived;
    public event Action<PartyPresence>? PartyPresenceReceived;
    public void SendSocial(SocialCommand command) { if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered); }
    public event Action<PvpState>? PvpStateReceived;
    public event Action<PvpPublicState>? PvpPublicReceived;
    public event Action<PvpResult>? PvpResultReceived;
    public event Action<PickupChannelState>? PickupChannelReceived;
    public event Action<PvpLootState>? PvpLootReceived;
    public PvpZoneState? LatestPvpZone {get;private set;}
    public void SendPvp(PvpCommand command){if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}
    public void SendEconomy(EconomyCommand command) {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}
    public event Action<TradeState>? TradeStateReceived;
    public event Action<TradeResult>? TradeResultReceived;
    public Dictionary<NetworkEntityId,PlayerSpawn> KnownPlayers { get; } = new();
    public void SendTrade(TradeCommand command) { if(_handshakeComplete) SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered); }
    public void SendRepair(RepairCommand command) { if(_handshakeComplete) SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered); }
    public event Action<CraftResult>? CraftResultReceived;
    public event Action<ResourceNodeState>? ResourceNodeReceived;
    public event Action<ResourceNodeDespawn>? ResourceNodeLeft;
    public void SendCraft(CraftCommand command) { if(_handshakeComplete) SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered); }
    public StarterZoneState? LatestStarterZone { get; private set; }
    public event Action<ExplorationState>? ExplorationReceived;
    public ExplorationState? LatestExploration { get; private set; }
    public WorldNodeState? LatestWorldNode { get; private set; }
    public event Action<WorldNodeState>? WorldNodeReceived;
    public event Action<WorldNodeResult>? WorldNodeResultReceived;
    public void SendWorldNode(WorldNodeCommand command)
    { if(_handshakeComplete) SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered); }
    public event Action<ProfessionState>? ProfessionReceived;
    public event Action<ProfessionResult>? ProfessionResultReceived;
    public void SendProfession(ProfessionCommand command)
    { if (_handshakeComplete) SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered); }
    public event Action<ProgressionState>? ProgressionReceived;
    public event Action<ProgressionResult>? ProgressionResultReceived;
    public event Action<StatPreview>? StatPreviewReceived;
    public event Action<DefenseState>? DefenseReceived;
    public event Action<ControlState>? ControlStateReceived;
    public ControlState? LatestControlState {get;private set;}
    // NC: public observer state is distinct from the owner's private defense resources.
    public event Action<AvatarState>? AvatarReceived;
    public void SendEmote(EmoteCommand command) { if (_handshakeComplete) SendGame(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered); }
    public DefenseState? LatestDefense { get; private set; }
    public Dictionary<NetworkEntityId,QuestNpcSpawn> QuestNpcs {get;}=new();
    public QuestJournal? LatestQuestJournal {get;private set;}
    public event Action<QuestNpcSpawn>? QuestNpcReceived;
    public event Action<QuestReply>? QuestReplyReceived;
    public event Action<QuestJournal>? QuestJournalReceived;
    public void SendQuest(QuestCommand command) { if(_handshakeComplete) SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered); }
    public void SendDefense(DefenseCommand command)
    { if (_handshakeComplete) SendGame(NetworkProtocol.Write(command with {ObservedServerTick=LatestServerTick,InputOrder=NextInputOrder()}),command.Action==DefenseAction.Block ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered); }
    public void SendProgression(ProgressionCommand command)
    { if (_handshakeComplete) SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered); }
    public event Action<EchoSpawn>? EchoSpawned;
    public event Action<EchoLoadout>? EchoLoadoutReceived;
    public event Action<EchoAction>? EchoActionReceived;
    public event Action<EchoSignatureResult>? EchoSignatureResultReceived;
    public event Action<EchoRosterPage>? EchoRosterReceived;
    public event Action<EchoSummonResult>? EchoSummonReceived;
    public event Action<EchoRecallResult>? EchoRecallReceived;
    public event Action<EchoRestoreResult>? EchoRestoreReceived;
    public EchoRosterPage? LatestEchoRoster {get;private set;}
    public void SendEchoRoster(EchoRosterRequest command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}
    public void SendEchoSummon(EchoSummonCommand command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command with {InputOrder=NextInputOrder()}),DeliveryMethod.ReliableOrdered);}
    public void SendEchoRecall(EchoRecallCommand command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}
    public void SendEchoSummonControl(EchoSummonControlCommand command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}
    public void SendEchoRestore(EchoRestoreCommand command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}

    public void SendEchoSignature(EchoSignatureCommand command)
    {
        if (_handshakeComplete) SendGame(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered);
    }
    public void SendEchoControl(EchoSignatureControlCommand command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),command.Kind==EchoSignatureControlKind.Aim?DeliveryMethod.Unreliable:DeliveryMethod.ReliableOrdered);}

    // Can be refreshed during a development run after the large regional baseline has loaded.
    internal void ApplyNetworkSimulation()
    {
        _client.SimulateLatency = SimulateNetworkConditions;
        _client.SimulationMinLatency = Math.Clamp(SimulatedMinLatencyMs,0,500);
        _client.SimulationMaxLatency = Math.Clamp(SimulatedMaxLatencyMs,_client.SimulationMinLatency,500);
        _client.SimulatePacketLoss = SimulateNetworkConditions && SimulatedPacketLossPercent > 0;
        _client.SimulationPacketLossChance = Math.Clamp(SimulatedPacketLossPercent,0,100);
    }

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

        ApplyNetworkSimulation();

        if (!_client.Start())
        {
            GD.PushError("Failed to start the network client.");
            return;
        }

    }

    public override void _Process(double delta) => _client?.PollEvents();

    public void ConnectToServer()
        => ConnectToServerAt(NetworkConstants.Port);

    public void ConnectToServerAt(int port)
    {
        if(port is <1 or >ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(port));
        LastConnectionError = "";
        if (!_identityUsable || _client is null || !_client.IsRunning)
        {
            ReportConnectionFailure("Не удалось начать вход. Проверь локальный профиль и настройки клиента.");
            return;
        }
        if (_client.FirstPeer is not null)
            return;

        GD.Print("Connecting to server...");
        foreach (var argument in OS.GetCmdlineUserArgs())
            if (argument.StartsWith("--server-port=", StringComparison.Ordinal) &&
                int.TryParse(argument[14..], out var configured) && configured is > 0 and <= ushort.MaxValue) port = configured;
        _client.Connect("127.0.0.1", port, NetworkConstants.ConnectionKey);
    }

    public void SendMove(MoveCommand command)
    {
        if (!_handshakeComplete || _serverPeer is null || !RegionActive)
            return;

        SendGame(NetworkProtocol.Write(command), command.Kind == MovementCommandKind.Cursor
            ? DeliveryMethod.Sequenced : DeliveryMethod.ReliableOrdered);
        MovementSent?.Invoke(command);
    }

    public void RejectRegionContent()
    {
        ReportConnectionFailure("Карта несовместима с клиентом. Обнови клиент и сервер."); // NC
        _handshakeComplete = false;
        if (_serverPeer is not null) _client?.DisconnectPeer(_serverPeer);
    }

    public void SendAttack(AttackCommand command)
    {
        if (_handshakeComplete && _serverPeer is not null)
            SendGame(NetworkProtocol.Write(command with { InputOrder=NextInputOrder() }), DeliveryMethod.ReliableOrdered);
    }

    public void SendAbility(AbilityCommand command)
    {
        if (_handshakeComplete && _serverPeer is not null)
            SendGame(NetworkProtocol.Write(command with { InputOrder=NextInputOrder() }), DeliveryMethod.ReliableOrdered);
    }
    public void SendBasicControl(BasicControlCommand command)
    {
        if(_handshakeComplete && _serverPeer is not null)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);
    }
    public void SendActionCancel(ActionCancelCommand command)
    {
        if(_handshakeComplete && _serverPeer is not null)
            SendGame(NetworkProtocol.Write(command with {ObservedServerTick=LatestServerTick}),DeliveryMethod.ReliableOrdered);
    }
    public void SendAbilityControl(AbilityControlCommand command)
    {
        if(_handshakeComplete && _serverPeer is not null)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);
    }
    public void SendCombatJournalRequest(CombatJournalRequest command)=>SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);
    public void SendDevelopmentRevive(DevelopmentReviveCommand command)
    {
        if (_handshakeComplete && CanDevelopmentRevive)
            SendGame(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered);
    }
    public void SendPickup(PickupCommand command)
    {
        if (_handshakeComplete) SendGame(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered);
    }
    public void SendInventory(InventoryCommand command)
    {
        if (_handshakeComplete && _serverPeer is not null)
            SendGame(NetworkProtocol.Write(command), DeliveryMethod.ReliableOrdered);
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
        LoggedOut=false;LogoutPending=false;_logoutSequence=0;
        _serverPeer = peer;
        var buildVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var hello = new ClientHello(NetworkConstants.ProtocolVersion, buildVersion, _identityToken);

        peer.Send(NetworkProtocol.Write(hello), DeliveryMethod.ReliableOrdered);
        GD.Print("Connected. Sending protocol handshake...");
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        var extra=disconnectInfo.AdditionalData;
        if(extra.AvailableBytes is >0 and <=NetworkConstants.MaxHandshakePacketBytes)
        {
            var start=extra.Position;
            var logoutReader=new NetDataReader(extra.GetRemainingBytes());
            if(NetworkProtocol.TryReadMessageType(logoutReader,out var extraType) && extraType==NetworkMessageType.LogoutResult &&
                NetworkProtocol.TryReadLogoutResult(logoutReader,out var logout) && logout.Sequence==_logoutSequence && logout.Outcome==LogoutOutcome.Accepted)
            {LoggedOut=true;LogoutPending=false;LogoutReceived?.Invoke(logout);}
            extra.SetPosition(start);
        }
        _serverPeer = null;
        _handshakeComplete = false;
        LocalPlayerId = PlayerId.Invalid;
        CurrentRegion = null;
        KnownPlayers.Clear();
        KnownLife.Clear();
        LatestTavern=null;_respawnSequence=0;_tavernSequence=0;_respecSequence=0;
        LatestEchoRoster=null;
        _sustainedEffects.Clear();
        _basicActions.Clear();
        _abilityLoadoutParts.Clear();
        LatestBasicInput=null;
        LatestCombatJournalIndex=null;
        LatestControlState=null;
        CanDevelopmentRevive = false;
        Navigation = null; LatestExploration=null; LatestWorldNode=null; LatestStarterZone=null; LatestPvpZone=null; LatestDefense=null; CraftRecipes.Clear(); ResourceNodes.Clear(); QuestNpcs.Clear(); LatestQuestJournal=null;LatestActivities=null;
        LatestServerTick = 0;
        if (TryReadDisconnectRejection(disconnectInfo, out var rejection))
        {
            ReportConnectionFailure(RejectionMessage(rejection.Code)); // NC
            GD.PushError($"Server rejected connection ({rejection.Code}): {rejection.Reason}");
        }
        else if (!LoggedOut && LastConnectionError.Length == 0)
            ReportConnectionFailure("Соединение с сервером потеряно или сервер недоступен. Попробуй войти снова.");
        Disconnected?.Invoke();
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

            if (messageType == NetworkMessageType.RegionEnter)
            {
                if (!_handshakeComplete || deliveryMethod != DeliveryMethod.ReliableOrdered ||
                    !NetworkProtocol.TryReadRegionEnter(reader, out var entry))
                { DisconnectMalformed(peer); return; }
                if (entry.Epoch <= (CurrentRegion?.Epoch ?? 0)) return;
                ResetRegion(entry);
                return;
            }
            if (messageType == NetworkMessageType.RegionPacket)
            {
                if (!NetworkProtocol.TryReadRegionHeader(reader, out var epoch, out messageType))
                { DisconnectMalformed(peer); return; }
                // Unreliable packets can overtake the reliable entry or arrive from the previous region.
                if (!_handshakeComplete || epoch != CurrentRegion?.Epoch) return;
            }
            else if (CurrentRegion is not null && messageType != NetworkMessageType.ServerReject)
            { DisconnectMalformed(peer); return; }

            switch (messageType)
            {
                case NetworkMessageType.RespecQuote:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadRespecQuote(reader,out var resetQuote))
                        RespecQuoteReceived?.Invoke(resetQuote);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.RespecResult:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadRespecResult(reader,out var resetResult))
                        RespecResultReceived?.Invoke(resetResult);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.TavernState:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadTavernState(reader,out var home))
                    {LatestTavern=home;TavernReceived?.Invoke(home);}else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.TavernResult:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadTavernResult(reader,out var tavernResult))
                        TavernResultReceived?.Invoke(tavernResult);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ActorLifeState:
                    if(_handshakeComplete && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadActorLifeState(reader,out var life))
                    {
                        if(!KnownLife.TryGetValue(life.EntityId,out var previousLife) || life.ServerTick==previousLife.ServerTick ||
                            Content.Shared.Movement.MovementSimulation.IsSequenceNewer(life.ServerTick,previousLife.ServerTick))
                        {KnownLife[life.EntityId]=life;LifeReceived?.Invoke(life);}
                    }
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.RespawnResult:
                    if(_handshakeComplete && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadRespawnResult(reader,out var respawn))RespawnReceived?.Invoke(respawn);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.LogoutResult:
                    if(_handshakeComplete && deliveryMethod==DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadLogoutResult(reader,out var logout) &&
                        logout.Sequence==_logoutSequence && logout.Outcome==LogoutOutcome.InCombat)
                    {LogoutPending=false;LogoutReceived?.Invoke(logout);}
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.SurfaceChunk:
                    if (deliveryMethod != DeliveryMethod.ReliableOrdered || Navigation is null || CurrentRegion is not { HasSurface: true } surfaceRegion || !NetworkProtocol.TryReadSurfaceChunk(reader, out var chunk)) { DisconnectMalformed(peer); break; }
                    try { if (_surfaceAssembly.Add(chunk) is { } geometry) { if (geometry.Hash != surfaceRegion.GeometryHash) throw new InvalidDataException("Wrong region surface."); Navigation.AttachSurface(geometry); NavigationReceived?.Invoke(Navigation); } }
                    catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or ArgumentException) { DisconnectMalformed(peer); }
                    break;
                case NetworkMessageType.RegionBaseline:
                    if (deliveryMethod == DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadRegionLoadState(reader, messageType, out var baseline) && baseline.GeometryHash == CurrentRegion?.GeometryHash && KnownPlayers.Values.Any(p => p.PlayerId == LocalPlayerId))
                        SendGame(NetworkProtocol.Write(new RegionLoadState(NetworkMessageType.RegionApplied, baseline.GeometryHash)), DeliveryMethod.ReliableOrdered);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.RegionActivated:
                    if (deliveryMethod == DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadRegionLoadState(reader, messageType, out var activated) && activated.GeometryHash == CurrentRegion?.GeometryHash) RegionActive = true;
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AvatarState:
                    if (_handshakeComplete && NetworkProtocol.TryReadAvatarState(reader, out var avatar)) AvatarReceived?.Invoke(avatar); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.SocialRoster:
                    if(_handshakeComplete&&NetworkProtocol.TryReadSocialRoster(reader,out var roster))SocialRosterReceived?.Invoke(roster);else DisconnectMalformed(peer);break;
                case NetworkMessageType.SocialInvites:
                    if(_handshakeComplete&&NetworkProtocol.TryReadSocialInvites(reader,out var invites))SocialInvitesReceived?.Invoke(invites);else DisconnectMalformed(peer);break;
                case NetworkMessageType.SocialResult:
                    if(_handshakeComplete&&NetworkProtocol.TryReadSocialResult(reader,out var socialResult))SocialResultReceived?.Invoke(socialResult);else DisconnectMalformed(peer);break;
                case NetworkMessageType.PartyPresence:
                    if(_handshakeComplete&&NetworkProtocol.TryReadPartyPresence(reader,out var presence))PartyPresenceReceived?.Invoke(presence);else DisconnectMalformed(peer);break;
                case NetworkMessageType.PvpState:
                    if(_handshakeComplete&&NetworkProtocol.TryReadPvpState(reader,out var pvpState))PvpStateReceived?.Invoke(pvpState);else DisconnectMalformed(peer);break;
                case NetworkMessageType.PvpPublicState:
                    if(_handshakeComplete&&NetworkProtocol.TryReadPvpPublicState(reader,out var pvpPublic))PvpPublicReceived?.Invoke(pvpPublic);else DisconnectMalformed(peer);break;
                case NetworkMessageType.PvpResult:
                    if(_handshakeComplete&&NetworkProtocol.TryReadPvpResult(reader,out var pvpResult))PvpResultReceived?.Invoke(pvpResult);else DisconnectMalformed(peer);break;
                case NetworkMessageType.PickupChannelState:
                    if(_handshakeComplete&&NetworkProtocol.TryReadPickupChannelState(reader,out var channel))PickupChannelReceived?.Invoke(channel);else DisconnectMalformed(peer);break;
                case NetworkMessageType.PvpZoneState:
                    if(_handshakeComplete&&NetworkProtocol.TryReadPvpZoneState(reader,out var pvpZone))LatestPvpZone=pvpZone;else DisconnectMalformed(peer);break;
                case NetworkMessageType.PvpLootState:
                    if(_handshakeComplete&&NetworkProtocol.TryReadPvpLootState(reader,out var pvpLoot))PvpLootReceived?.Invoke(pvpLoot);else DisconnectMalformed(peer);break;
                case NetworkMessageType.ItemConditionState:
                    if(_handshakeComplete && NetworkProtocol.TryReadItemConditionState(reader,out var condition)) ItemConditionReceived?.Invoke(condition); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EconomyState:
                    if(_handshakeComplete && NetworkProtocol.TryReadEconomyState(reader,out var economyState))EconomyStateReceived?.Invoke(economyState);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EconomyQuote:
                    if(_handshakeComplete && NetworkProtocol.TryReadEconomyQuote(reader,out var economyQuote))EconomyQuoteReceived?.Invoke(economyQuote);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EconomyResult:
                    if(_handshakeComplete && NetworkProtocol.TryReadEconomyResult(reader,out var economyResult))EconomyResultReceived?.Invoke(economyResult);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.MarketState:
                    if(_handshakeComplete && NetworkProtocol.TryReadMarketState(reader,out var marketState))MarketStateReceived?.Invoke(marketState);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.TradeState:
                    if(_handshakeComplete && NetworkProtocol.TryReadTradeState(reader,out var tradeState)) TradeStateReceived?.Invoke(tradeState); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.TradeResult:
                    if(_handshakeComplete && NetworkProtocol.TryReadTradeResult(reader,out var tradeResult)) TradeResultReceived?.Invoke(tradeResult); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.RepairQuote:
                    if(_handshakeComplete && NetworkProtocol.TryReadRepairQuote(reader,out var repairQuote)) RepairQuoteReceived?.Invoke(repairQuote); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.RepairResult:
                    if(_handshakeComplete && NetworkProtocol.TryReadRepairResult(reader,out var repairResult)) RepairResultReceived?.Invoke(repairResult); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.CraftRecipeState:
                    if(_handshakeComplete && NetworkProtocol.TryReadCraftRecipeState(reader,out var recipe) && (CraftRecipes.ContainsKey(recipe.Id) || CraftRecipes.Count<8)) CraftRecipes[recipe.Id]=recipe; else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ResourceNodeState:
                    if(_handshakeComplete && NetworkProtocol.TryReadResourceNodeState(reader,out var resource) && Navigation?.IsWalkable(resource.Position)==true && (ResourceNodes.ContainsKey(resource.Id) || ResourceNodes.Count<16)) { ResourceNodes[resource.Id]=resource; ResourceNodeReceived?.Invoke(resource); } else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ResourceNodeDespawn:
                    if(_handshakeComplete && NetworkProtocol.TryReadResourceNodeDespawn(reader,out var resourceLeft)) { ResourceNodes.Remove(resourceLeft.Id); ResourceNodeLeft?.Invoke(resourceLeft); } else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.CraftState:
                    if(_handshakeComplete && NetworkProtocol.TryReadCraftState(reader,out var craftState)) CraftStateReceived?.Invoke(craftState); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.CraftResult:
                    if(_handshakeComplete && NetworkProtocol.TryReadCraftResult(reader,out var craftResult)) CraftResultReceived?.Invoke(craftResult); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.StarterZoneState:
                    if (_handshakeComplete && NetworkProtocol.TryReadStarterZoneState(reader,out var zone)) LatestStarterZone=zone;
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ExplorationState:
                    if (_handshakeComplete && NetworkProtocol.TryReadExplorationState(reader,out var exploration) && Navigation is { } grid && exploration.Width==grid.Width && exploration.Height==grid.Height)
                    { LatestExploration=exploration; ExplorationReceived?.Invoke(exploration); }
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.WorldNodeState:
                    if(_handshakeComplete && NetworkProtocol.TryReadWorldNodeState(reader,out var node))
                    { if(LatestWorldNode is null || node.Revision>LatestWorldNode.Value.Revision) { LatestWorldNode=node; WorldNodeReceived?.Invoke(node); } }
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.WorldNodeResult:
                    if(_handshakeComplete && NetworkProtocol.TryReadWorldNodeResult(reader,out var nodeResult)) WorldNodeResultReceived?.Invoke(nodeResult); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ProfessionState:
                    if (_handshakeComplete && NetworkProtocol.TryReadProfessionState(reader,out var profession)) ProfessionReceived?.Invoke(profession);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ProfessionResult:
                    if (_handshakeComplete && NetworkProtocol.TryReadProfessionResult(reader,out var professionResult)) ProfessionResultReceived?.Invoke(professionResult);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.DefenseState:
                    if (_handshakeComplete && NetworkProtocol.TryReadDefenseState(reader,out var defense) &&
                        (LatestDefense is not { } previousDefense || previousDefense.OwnerId!=defense.OwnerId || Content.Shared.Movement.MovementSimulation.IsSequenceNewer(defense.ServerTick,previousDefense.ServerTick)))
                    { LatestDefense=defense; DefenseReceived?.Invoke(defense); }
                    break;
                case NetworkMessageType.ActivityState:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadActivityState(reader,out var activities))
                    {LatestActivities=activities;ActivitiesReceived?.Invoke(activities);}else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ActivityResult:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadActivityResult(reader,out var activityResult))ActivityResultReceived?.Invoke(activityResult);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.QuestNpcSpawn:
                    if(_handshakeComplete && NetworkProtocol.TryReadQuestNpcSpawn(reader,out var questNpc) && (QuestNpcs.Count<32 || QuestNpcs.ContainsKey(questNpc.EntityId)))
                    { QuestNpcs[questNpc.EntityId]=questNpc; QuestNpcReceived?.Invoke(questNpc); } else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.QuestReply:
                    if(_handshakeComplete && NetworkProtocol.TryReadQuestReply(reader,out var questReply)) QuestReplyReceived?.Invoke(questReply); else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.QuestJournal:
                    if(_handshakeComplete && NetworkProtocol.TryReadQuestJournal(reader,out var journal)) { LatestQuestJournal=journal; QuestJournalReceived?.Invoke(journal); } else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.StatPreview:
                    if (_handshakeComplete && NetworkProtocol.TryReadStatPreview(reader,out var statPreview)) StatPreviewReceived?.Invoke(statPreview);
                    break;
                case NetworkMessageType.ProgressionState:
                    if (_handshakeComplete && NetworkProtocol.TryReadProgressionState(reader,out var progression)) ProgressionReceived?.Invoke(progression);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ProgressionResult:
                    if (_handshakeComplete && NetworkProtocol.TryReadProgressionResult(reader,out var progressionResult)) ProgressionResultReceived?.Invoke(progressionResult);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EchoSpawn:
                    if (_handshakeComplete && Navigation is not null && NetworkProtocol.TryReadEchoSpawn(reader, out var echoSpawn) && Navigation.IsOnSurface(new System.Numerics.Vector3(echoSpawn.Position.X,echoSpawn.Height,echoSpawn.Position.Y))) EchoSpawned?.Invoke(echoSpawn);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EchoLoadout:
                    if (_handshakeComplete && NetworkProtocol.TryReadEchoLoadout(reader, out var echoLoadout)) EchoLoadoutReceived?.Invoke(echoLoadout);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EchoRosterPage:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadEchoRosterPage(reader,out var echoRoster))
                    {LatestEchoRoster=echoRoster;EchoRosterReceived?.Invoke(echoRoster);}else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EchoSummonResult:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadEchoSummonResult(reader,out var echoSummon))
                        EchoSummonReceived?.Invoke(echoSummon);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EchoRecallResult:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadEchoRecallResult(reader,out var echoRecall))
                        EchoRecallReceived?.Invoke(echoRecall);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EchoRestoreResult:
                    if(_handshakeComplete&&deliveryMethod==DeliveryMethod.ReliableOrdered&&NetworkProtocol.TryReadEchoRestoreResult(reader,out var echoRestore))
                        EchoRestoreReceived?.Invoke(echoRestore);else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EchoAction:
                    if (_handshakeComplete && NetworkProtocol.TryReadEchoAction(reader, out var echoAction)) EchoActionReceived?.Invoke(echoAction);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.EchoSignatureResult:
                    if (_handshakeComplete && NetworkProtocol.TryReadEchoSignatureResult(reader, out var echoResult)) EchoSignatureResultReceived?.Invoke(echoResult);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ServerWelcome:
                    HandleWelcome(peer, reader);
                    break;
                case NetworkMessageType.ServerReject:
                    HandleReject(peer, reader);
                    break;
                case NetworkMessageType.PlayerSpawn:
                    if (Navigation is not null && NetworkProtocol.TryReadPlayerSpawn(reader, out var spawn) &&
                        Navigation.IsOnSurface(new System.Numerics.Vector3(spawn.Position.X, spawn.Height, spawn.Position.Y)))
                    {
                        if (Content.Shared.Movement.MovementSimulation.IsSequenceNewer(spawn.ServerTick, LatestServerTick))
                            LatestServerTick = spawn.ServerTick;
                        if(KnownPlayers.Count<128 || KnownPlayers.ContainsKey(spawn.EntityId)) KnownPlayers[spawn.EntityId]=spawn;
                        PlayerSpawned?.Invoke(spawn);
                    }
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.RegionNavigation:
                    if (_handshakeComplete &&
                        NetworkProtocol.TryReadRegionNavigation(reader, out var region))
                    {
                        if(Navigation is null) Navigation=new NavigationGrid(region);
                        else
                        {
                            try { Navigation.ApplyOpening(region); }
                            catch(ArgumentException) { DisconnectMalformed(peer); break; }
                        }
                        if (CurrentRegion?.HasSurface != true || Navigation.Surface is not null) NavigationReceived?.Invoke(Navigation);
                    }
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.PlayerDespawn:
                    if (NetworkProtocol.TryReadPlayerDespawn(reader, out var despawn))
                    {
                        QuestNpcs.Remove(despawn.EntityId);
                        KnownPlayers.Remove(despawn.EntityId);
                        _basicActions.Remove(despawn.EntityId);
                        KnownLife.Remove(despawn.EntityId);
                        PlayerDespawned?.Invoke(despawn);
                    }
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.FollowState:
                    if (_handshakeComplete && deliveryMethod == DeliveryMethod.ReliableOrdered && NetworkProtocol.TryReadFollowState(reader, out var follow)) FollowReceived?.Invoke(follow);
                    else DisconnectMalformed(peer);
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
                case NetworkMessageType.BasicActionState:
                    if(_handshakeComplete && NetworkProtocol.TryReadBasicActionState(reader,out var basic))
                    {if(_basicActions.Accept(basic,deliveryMethod==DeliveryMethod.ReliableOrdered))BasicActionReceived?.Invoke(basic);}
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.BasicInputState:
                    if(_handshakeComplete && NetworkProtocol.TryReadBasicInputState(reader,out var basicInput))
                    {
                        if(LatestBasicInput is not {} previousBasic || previousBasic.ActorId!=basicInput.ActorId ||
                            basicInput.ServerTick==previousBasic.ServerTick || MovementSimulation.IsSequenceNewer(basicInput.ServerTick,previousBasic.ServerTick))
                        {LatestBasicInput=basicInput;BasicInputReceived?.Invoke(basicInput);}
                    }
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AttackResult:
                    if (_handshakeComplete && NetworkProtocol.TryReadAttackResult(reader, out var result))
                        AttackResultReceived?.Invoke(result);
                    else
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AbilityLoadout:
                    if (!_handshakeComplete || !NetworkProtocol.TryReadAbilityLoadout(reader, out var loadout) || !ReceiveAbilityLoadout(loadout))
                        DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AbilityResult:
                    if (_handshakeComplete && NetworkProtocol.TryReadAbilityResult(reader, out var abilityResult))
                        AbilityResultReceived?.Invoke(abilityResult);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.AbilityEffectState:
                    if (_handshakeComplete && NetworkProtocol.TryReadAbilityEffectState(reader, out var effect))
                    {if(_sustainedEffects.Accept(effect,deliveryMethod==DeliveryMethod.ReliableOrdered))AbilityEffectReceived?.Invoke(effect);}
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
                case NetworkMessageType.CombatFeedback:
                    if(_handshakeComplete && NetworkProtocol.TryReadCombatFeedback(reader,out var feedback))CombatFeedbackReceived?.Invoke(feedback);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ControlFeedback:
                    if(_handshakeComplete && NetworkProtocol.TryReadControlFeedback(reader,out var controlFeedback))ControlFeedbackReceived?.Invoke(controlFeedback);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.ControlState:
                    if(!_handshakeComplete || !NetworkProtocol.TryReadControlState(reader,out var controlState) ||
                        !KnownPlayers.TryGetValue(controlState.OwnerId,out var controlOwner) || controlOwner.PlayerId!=LocalPlayerId)
                    {DisconnectMalformed(peer);break;}
                    if(LatestControlState is not {} previousControl || previousControl.OwnerId!=controlState.OwnerId ||
                        Content.Shared.Movement.MovementSimulation.IsSequenceNewer(controlState.ServerTick,previousControl.ServerTick))
                    {LatestControlState=controlState;ControlStateReceived?.Invoke(controlState);}
                    break;
                case NetworkMessageType.CombatJournalIndex:
                    if(_handshakeComplete && NetworkProtocol.TryReadCombatJournalIndex(reader,out var journalIndex) &&
                        KnownPlayers.TryGetValue(journalIndex.OwnerId,out var journalOwner) && journalOwner.PlayerId==LocalPlayerId)
                    {LatestCombatJournalIndex=journalIndex;CombatJournalIndexReceived?.Invoke(journalIndex);}
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.CombatJournalChunk:
                    if(_handshakeComplete && NetworkProtocol.TryReadCombatJournalChunk(reader,out var journalChunk) &&
                        KnownPlayers.TryGetValue(journalChunk.OwnerId,out var chunkOwner) && chunkOwner.PlayerId==LocalPlayerId)
                        CombatJournalChunkReceived?.Invoke(journalChunk);
                    else DisconnectMalformed(peer);
                    break;
                case NetworkMessageType.CombatJournalResult:
                    if(_handshakeComplete && NetworkProtocol.TryReadCombatJournalResult(reader,out var journalResult))CombatJournalResultReceived?.Invoke(journalResult);
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
                ReportConnectionFailure("Не удалось сохранить локальный профиль. Закрой другие клиенты с тем же профилем."); // NC
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
        { // NC: translate rejection codes without exposing raw diagnostic text.
            ReportConnectionFailure(RejectionMessage(rejection.Code));
            GD.PushError($"Server rejected connection ({rejection.Code}): {rejection.Reason}");
        }
        else
            GD.PushError("Server rejected connection with malformed details.");

        _client.DisconnectPeer(peer);
    }

    private void DisconnectMalformed(NetPeer peer)
    {
        ReportConnectionFailure("Получены несовместимые данные сервера. Проверь версии клиента и сервера."); // NC
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

    // NC start: local presentation only; the wire protocol and identity ownership are unchanged.
    private void ReportConnectionFailure(string message)
    {
        LastConnectionError = message;
        ConnectionFailed?.Invoke(message);
    }

    private static string RejectionMessage(HandshakeRejectCode code) => code switch
    {
        HandshakeRejectCode.UnsupportedProtocol => "Версии клиента и сервера различаются. Обнови игру.",
        HandshakeRejectCode.InvalidIdentity => "Сервер не распознал сохранённый профиль. Профиль не был заменён.",
        HandshakeRejectCode.CharacterInUse => "Персонаж уже в игре. Закрой другую сессию и попробуй снова.",
        HandshakeRejectCode.PersistenceUnavailable => "Сервер не смог загрузить персонажа. Попробуй позже.",
        HandshakeRejectCode.DevelopmentOnly => "Этот сервер поддерживает только локальный вход для разработки.",
        HandshakeRejectCode.ServerBusy => "Сервер занят. Попробуй позже.",
        _ => "Сервер отклонил вход. Проверь версии клиента и сервера."
    };
    // NC end

    private static void OnNetworkError(
        System.Net.IPEndPoint endpoint,
        System.Net.Sockets.SocketError error)
    {
        GD.PushWarning($"Network error from {endpoint}: {error}.");
    }
}
