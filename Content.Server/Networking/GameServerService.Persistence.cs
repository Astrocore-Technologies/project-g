using System.Net;
using Content.Server.Persistence;
using Content.Shared.Network;
using LiteNetLib;
using Microsoft.Extensions.Logging;

namespace Content.Server.Networking;

public sealed partial class GameServerService
{
    private readonly ICharacterStore? _characters;
    private readonly int _maxSessions;
    private readonly Dictionary<int, CharacterSession> _sessions = new();
    private readonly Dictionary<int, Login> _logins = new();
    private readonly Dictionary<int, Intentions> _intentions = new();
    private readonly HashSet<int> _departed = new();
    private readonly List<Task> _closing = new();
    private SocialSession? _socialSession;
    private SocialSave? _checkpointSocial;
    private IReadOnlyList<ItemWearChange> _checkpointWear=[];
    private IReadOnlyList<WorldNodeSave> _checkpointWorlds = [];
    private Task? _checkpoint;
    private CancellationTokenSource? _checkpointDeadline;
    private List<CharacterSave>? _checkpointChanges;
    private int[] _checkpointDepartures=[];
    private readonly HashSet<int> _departurePrepared=new();
    private readonly Dictionary<int,LogoutResult> _logoutApprovals=new();

    private sealed record Login(NetPeer Peer, ClientHello Hello, PlayerId Player, Task<CharacterSession> Task,
        CancellationTokenSource Deadline,IDisposable WearLoad);
    private sealed class Intentions
    {
        public SocialCommand? Social;
        public PvpCommand? Pvp;
        public CraftCommand? Craft;
        public RepairCommand? Repair;
        public TradeCommand? Trade;
        public EconomyCommand? Economy;
        public WorldNodeCommand? Node;
        public WorldNodeCommand? NodeSecond;
        public MoveCommand? Move;
        public EmoteCommand? Emote; // NC: bounded cosmetic intention, no persistence payload.
        public QuestCommand? Quest;
        public LogoutCommand? Logout;
        public RespawnCommand? Respawn;
        public CombatJournalRequest? Journal;
        public InventoryCommand? Inventory;
        public PickupCommand? Pickup;
        public DevelopmentReviveCommand? Revive;
        public readonly EchoSignatureIntentBuffer Echo=new();
        public readonly EchoSignatureControlBuffer EchoControl=new();
        public EchoRosterRequest? EchoRoster;
        public EchoSummonCommand? EchoSummon;
        public EchoRecallCommand? EchoRecall;
        public EchoSummonControlCommand? EchoSummonControl;
        public EchoRestoreCommand? EchoRestore;
        public ProgressionCommand? Progression;
        public ProgressionCommand? ProgressionSecond;
        public ProfessionCommand? Profession;
        public ProfessionCommand? ProfessionSecond;
    }

    private void BeginLogin(NetPeer peer, ClientHello hello, PlayerId player)
    {
        if (!IPAddress.IsLoopback(peer.Address))
        {
            Reject(peer, HandshakeRejectCode.DevelopmentOnly, "Development identity is restricted to localhost.");
            return;
        }
        if(TryResumeCombat(peer,hello,player))return;
        if (_sessions.Count + _logins.Count + _closing.Count >= _maxSessions)
        {
            Reject(peer, HandshakeRejectCode.ServerBusy, "Development session budget is full.");
            return;
        }
        var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _logins.Add(peer.Id, new(peer, hello, player,
            _characters!.OpenAsync(hello.DevelopmentToken, (_regionalWorlds?.StartingWorld ?? _world).CreateInitialCharacter(), deadline.Token), deadline,
            _world.ItemWear.BeginLoad()));
    }

    private void CompleteLogins()
    {
        CompleteCombatResumes();
        if (_logins.Count == 0) return;
        foreach (var (connection, login) in _logins.ToArray())
        {
            if (!login.Task.IsCompleted) continue;
            _logins.Remove(connection);
            login.Deadline.Dispose();
            CharacterSession? session = null;
            var connected = _peers.TryGetValue(connection, out var peer) && ReferenceEquals(peer, login.Peer);
            try
            {
                session = login.Task.GetAwaiter().GetResult();
                if (!connected)
                {
                    _closing.Add(session.DisposeAsync().AsTask());
                    continue;
                }
                AcceptPlayer(login.Peer, login.Hello, login.Player, session);
                _sessions.Add(connection, session);
                _sessionCredentialHashes[connection]=CredentialHash(login.Hello.DevelopmentToken.Length>0?login.Hello.DevelopmentToken:session.IssuedToken);
                _logger.LogInformation("Character restored. CharacterId={CharacterId}, Revision={Revision}",
                    session.CharacterId, session.Revision);
            }
            catch (Exception exception)
            {
                if (session is not null) _closing.Add(session.DisposeAsync().AsTask());
                var (code, reason) = exception switch
                {
                    InvalidIdentityException => (HandshakeRejectCode.InvalidIdentity, "Unknown development identity; existing token was not replaced."),
                    CharacterInUseException => (HandshakeRejectCode.CharacterInUse, "Character is already online or finishing a save; retry shortly."),
                    _ => (HandshakeRejectCode.PersistenceUnavailable, "Character could not be restored; check server diagnostics.")
                };
                // Never log credentials, SQL parameters, connection strings or exception messages.
                _logger.LogWarning("Character login failed. ErrorType={ErrorType}", exception.GetType().Name);
                if (connected) Reject(login.Peer, code, reason);
            }
            finally{login.WearLoad.Dispose();}
        }
    }

    private void ApplyBufferedIntentions()
    {
        // Bounded one intention per type/session while waiting for disk; no growing packet queue.
        foreach (var (connection, intentions) in _intentions)
        {
            if (!_peers.ContainsKey(connection)) continue;
            var world = WorldFor(connection);
            if(intentions.Logout is {} logout)world.TryQueueLogout(connection,logout);
            if(intentions.Respawn is {} respawn)world.TryQueueRespawn(connection,respawn);
            if(intentions.Social is {} social)world.TryQueueSocial(connection,social);
            if(intentions.Pvp is {} pvp)world.TryQueuePvp(connection,pvp);
            if(intentions.Economy is { } economy) world.TryQueueEconomy(connection,economy);
            if(intentions.Trade is { } trade) world.TryQueueTrade(connection,trade);
            if(intentions.Repair is { } repair) world.TryQueueRepair(connection,repair);
            if (intentions.Craft is { } craft) world.TryQueueCraft(connection,craft);
            if (intentions.Node is { } node) world.TryQueueWorldNode(connection,node);
            if (intentions.NodeSecond is { } nodeSecond) world.TryQueueWorldNode(connection,nodeSecond);
            if (intentions.Move is { } move) world.ApplyReceivedMove(connection, move);
            if (intentions.Emote is { } emote) world.TryQueueEmote(connection,emote);
            if (intentions.Quest is { } quest) world.TryQueueQuest(connection,quest);
            if(intentions.Journal is {} journal)world.TryQueueJournalRequest(connection,journal);
            if (intentions.Inventory is { } inventory) world.TryQueueInventory(connection, inventory);
            if (intentions.Pickup is { } pickup) world.TryQueuePickup(connection, pickup);
            if (intentions.Profession is { } profession) world.TryQueueProfession(connection,profession);
            if (intentions.ProfessionSecond is { } professionSecond) world.TryQueueProfession(connection,professionSecond);
            if (intentions.Progression is { } progression) world.TryQueueProgression(connection,progression);
            if (intentions.ProgressionSecond is { } second) world.TryQueueProgression(connection,second);
            foreach(var echo in intentions.Echo.Commands)world.TryQueueEchoSignature(connection,echo);
            foreach(var control in intentions.EchoControl.Commands)world.TryQueueEchoControl(connection,control);
            if(intentions.EchoRoster is {} roster)world.TryQueueEchoRoster(connection,roster);
            if(intentions.EchoSummon is {} summon)world.TryQueueEchoSummon(connection,summon);
            if(intentions.EchoSummonControl is {} summonControl)world.TryQueueEchoSummonControl(connection,summonControl);
            if(intentions.EchoRecall is {} recall)world.TryQueueEchoRecall(connection,recall);
            if(intentions.EchoRestore is {} restore)world.TryQueueEchoRestore(connection,restore);
            if (intentions.Revive is { } revive && CanDevelopmentRevive(_peers[connection]))
                world.TryQueueDevelopmentRevive(connection, revive, authorized: true);
            intentions.Move = null;
        }
        _intentions.Clear();
    }

    private Intentions BufferIntentions(int connection)
    {
        if (!_intentions.TryGetValue(connection, out var intentions))
            _intentions.Add(connection, intentions = new());
        return intentions;
    }

    private bool BeginCheckpoint()
    {
        if (_characters is null) return false;
        var dirty = (_world.Social?.Dirty.Count ?? 0) != 0 || _departed.Count!=0 || _world.ItemWear.HasWrites;
        foreach (var world in _worlds) dirty |= world.PersistenceDirty.Count != 0 || world.WorldNodeDirty;
        if (!dirty) return false;
        var changes = new List<CharacterSave>();
        var changedConnections=new HashSet<int>(_departed);
        foreach(var world in _worlds)changedConnections.UnionWith(world.PersistenceDirty);
        foreach(var connection in changedConnections)
            if(_sessions.TryGetValue(connection,out var session))
            {
                var world=WorldFor(connection);
                changes.Add(new(session,world.CaptureCharacter(connection,includeSessionJournal:false),world.PickupClaims(connection)));
            }
        _checkpointWorlds = CaptureWorldWrites();
        _checkpointWear=_world.ItemWear.Capture();
        if (changes.Count == 0 && _checkpointWorlds.Count == 0 && (_world.Social?.Dirty.Count ?? 0) == 0 && _checkpointWear.Count==0) return false;
        _checkpointSocial=CaptureSocialWrite();
        _checkpointChanges = changes;
        _checkpointDepartures=_departed.ToArray();
        _checkpointDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Host cancellation must not interrupt an in-flight final checkpoint.
        // A synchronous adapter failure must also leave a failed barrier, never trigger an unfenced final save.
        try { _checkpoint = SaveCheckpointAsync(changes, _checkpointWorlds, _checkpointSocial, _checkpointDeadline.Token); }
        catch (Exception exception) { _checkpoint = Task.FromException(exception); }
        return true;
    }

    private void CompleteCheckpoint()
    {
        if (_checkpoint is not { IsCompleted: true }) return;
        // Failure is fatal: never publish uncommitted state or keep playing on a broken store.
        try { _checkpoint.GetAwaiter().GetResult(); }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Character checkpoint failed ({exception.GetType().Name}); no state was published.");
        }
        foreach (var change in _checkpointChanges!) change.Session.Revision++;
        CompleteWorldWrite();
        CompleteSocialWrite();
        CompleteWearWrite();
        foreach (var world in _worlds) world.GroundItems?.CommitClaims();
        _checkpoint = null; _checkpointChanges = null;
        _checkpointDeadline!.Dispose(); _checkpointDeadline = null;
        CompleteDepartures();
        BroadcastSnapshot();
    }

    private SocialSave? CaptureSocialWrite() => (_world.Social?.Dirty.Count ?? 0)>0 ? new(_socialSession ?? throw new InvalidOperationException("Missing social lease."),_world.Social!.Dirty.ToArray()) : null;
    private void CompleteSocialWrite()
    { if(_checkpointSocial is null)return; _checkpointSocial.Session.Revision++; _world.Social!.Commit(); _checkpointSocial=null; }
    private IReadOnlyList<WorldNodeSave> CaptureWorldWrites()
    {
        var writes = new List<WorldNodeSave>(2);
        foreach (var world in _worlds)
            if (world.WorldNodeDirty)
                writes.Add(new(_worldLeases[world.WorldNodeKey], world.CaptureWorldNode(), world.WorldNodeAudit.ToArray()));
        return writes;
    }

    private Task SaveCheckpointAsync(IReadOnlyList<CharacterSave> characters, IReadOnlyList<WorldNodeSave> worlds,
        SocialSave? social, CancellationToken token)
    {
        if(_checkpointWear.Count>0)
            return (_characters as ICombatCheckpointStore??throw new InvalidOperationException("Combat checkpoints require atomic item wear storage."))
                .SaveCombatCheckpointAsync(characters,worlds,social,_checkpointWear,token);
        if (_regions is not null)
            return ((IRegionalCharacterStore)_characters!).SaveRegionalCheckpointAsync(characters, worlds, social, token);
        return social is null ? _characters!.SaveWithWorldAsync(characters, worlds.SingleOrDefault(), token) :
            ((ISocialStore)_characters!).SaveSocialCheckpointAsync(characters, worlds.SingleOrDefault(), social, token);
    }

    private void CompleteWearWrite(){_world.ItemWear.Commit(_checkpointWear);_checkpointWear=[];}

    private void CompleteWorldWrite()
    {
        foreach (var write in _checkpointWorlds)
        {
            write.Session.Revision++;
            _worlds.Single(w => ReferenceEquals(_worldLeases[w.WorldNodeKey], write.Session)).CommitWorldNode(write.Session.Revision);
        }
        _checkpointWorlds = [];
    }
    private void CloseDepartedPlayers()
    {
        ExpireDetachedCombatants();
        foreach (var connection in _departed)
        {
            if(!_departurePrepared.Add(connection))continue;
            // Include cleanup and the final character image in the same fenced checkpoint.
            var world=WorldFor(connection);
            world.DisconnectPvp(connection);
            world.FinishDeparture(connection);
            world.ScheduleSocialOffline(connection);
        }
        if(_characters is null && _departed.Count!=0)
        {_checkpointDepartures=_departed.ToArray();CompleteDepartures();BroadcastSnapshot();}
    }

    private void CompleteDepartures()
    {
        foreach(var connection in _checkpointDepartures)
        {
            if(_logoutApprovals.Remove(connection,out var approval) && _peers.TryGetValue(connection,out var peer))
                _server.DisconnectPeer(peer,NetworkProtocol.Write(approval));
            _views.Remove(connection);_loading.Remove(connection);_intentions.Remove(connection);_receivedCombat.Remove(connection);
            _handshakes.RemoveConnection(connection);
            _sessionCredentialHashes.Remove(connection);
            RemoveRegionalPlayer(connection);
            if(_sessions.Remove(connection,out var session))_closing.Add(session.DisposeAsync().AsTask());
            _departed.Remove(connection);_departurePrepared.Remove(connection);
        }
        _checkpointDepartures=[];
    }

    private void CompleteClosings()
    {
        for (var i = _closing.Count - 1; i >= 0; i--)
            if (_closing[i].IsCompleted)
            {
                _closing[i].GetAwaiter().GetResult();
                _closing.RemoveAt(i);
            }
    }

    private async Task FlushCharactersAsync()
    {
        if (_characters is null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (_regions is not null)
            {
                try { await _regions.PendingCommit; }
                finally { _regions.TryCompleteTravel(out _); }
            }
            if (_checkpoint is not null)
            {
                await _checkpoint;
                foreach (var change in _checkpointChanges!) change.Session.Revision++;
                CompleteWorldWrite();
                CompleteSocialWrite();
                CompleteWearWrite();
                foreach (var world in _worlds) world.GroundItems?.CommitClaims();
                _checkpoint = null;
                CompleteDepartures();
            }
            var changes = new List<CharacterSave>(_sessions.Count);
            foreach (var (connection, session) in _sessions)
            {
                var world = WorldFor(connection);
                changes.Add(new(session, world.CaptureCharacter(connection,includeSessionJournal:false), world.PickupClaims(connection)));
            }
            _checkpointWorlds = CaptureWorldWrites();
            _checkpointSocial = CaptureSocialWrite();
            _checkpointWear=_world.ItemWear.Capture();
            await SaveCheckpointAsync(changes, _checkpointWorlds, _checkpointSocial, timeout.Token);
            CompleteWorldWrite();
            CompleteSocialWrite();
            CompleteWearWrite();
            await Task.WhenAll(_closing);
        }
        finally
        {
            _checkpointDeadline?.Dispose();
            // Drain late login completions too; otherwise a disconnected login could retain its DB lock.
            foreach (var login in _logins.Values)
            {
                try { await (await login.Task).DisposeAsync(); }
                catch { /* Login/disposal already failed; no runtime character was created. */ }
                finally { login.Deadline.Dispose();login.WearLoad.Dispose(); }
            }
            var cleanup = _sessions.Values.Select(session => session.DisposeAsync().AsTask()).ToList();
            cleanup.AddRange(_closing);
            await Task.WhenAll(cleanup);
            _sessions.Clear(); _logins.Clear();
            await DisposeWorldLeasesAsync();
        }
    }
}
