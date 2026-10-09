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
    private IReadOnlyList<WorldNodeSave> _checkpointWorlds = [];
    private Task? _checkpoint;
    private CancellationTokenSource? _checkpointDeadline;
    private List<CharacterSave>? _checkpointChanges;

    private sealed record Login(NetPeer Peer, ClientHello Hello, PlayerId Player, Task<CharacterSession> Task,
        CancellationTokenSource Deadline);
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
        public AttackCommand? Attack;
        public DefenseCommand? Defense;
        public QuestCommand? Quest;
        public AbilityCommand? Ability;
        public InventoryCommand? Inventory;
        public PickupCommand? Pickup;
        public DevelopmentReviveCommand? Revive;
        public EchoSignatureCommand? Echo;
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
            _characters!.OpenAsync(hello.DevelopmentToken, (_regionalWorlds?.StartingWorld ?? _world).CreateInitialCharacter(), deadline.Token), deadline));
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
        }
    }

    private void ApplyBufferedIntentions()
    {
        // Bounded one intention per type/session while waiting for disk; no growing packet queue.
        foreach (var (connection, intentions) in _intentions)
        {
            if (!_peers.ContainsKey(connection)) continue;
            var world = WorldFor(connection);
            if(intentions.Social is {} social)world.TryQueueSocial(connection,social);
            if(intentions.Pvp is {} pvp)world.TryQueuePvp(connection,pvp);
            if(intentions.Economy is { } economy) world.TryQueueEconomy(connection,economy);
            if(intentions.Trade is { } trade) world.TryQueueTrade(connection,trade);
            if(intentions.Repair is { } repair) world.TryQueueRepair(connection,repair);
            if (intentions.Craft is { } craft) world.TryQueueCraft(connection,craft);
            if (intentions.Node is { } node) world.TryQueueWorldNode(connection,node);
            if (intentions.NodeSecond is { } nodeSecond) world.TryQueueWorldNode(connection,nodeSecond);
            if (intentions.Move is { } move) world.TryApplyMove(connection, move);
            if (intentions.Attack is { } attack) world.TryQueueAttack(connection, attack);
            if (intentions.Defense is { } defense) world.TryQueueDefense(connection,defense);
            if (intentions.Quest is { } quest) world.TryQueueQuest(connection,quest);
            if (intentions.Ability is { } ability) world.TryQueueAbility(connection, ability, _peers[connection].Ping);
            if (intentions.Inventory is { } inventory) world.TryQueueInventory(connection, inventory);
            if (intentions.Pickup is { } pickup) world.TryQueuePickup(connection, pickup);
            if (intentions.Profession is { } profession) world.TryQueueProfession(connection,profession);
            if (intentions.ProfessionSecond is { } professionSecond) world.TryQueueProfession(connection,professionSecond);
            if (intentions.Progression is { } progression) world.TryQueueProgression(connection,progression);
            if (intentions.ProgressionSecond is { } second) world.TryQueueProgression(connection,second);
            if (intentions.Echo is { } echo) world.TryQueueEchoSignature(connection,echo);
            if (intentions.Revive is { } revive && CanDevelopmentRevive(_peers[connection]))
                world.TryQueueDevelopmentRevive(connection, revive, authorized: true);
            intentions.Move = null; intentions.Attack = null; intentions.Ability = null;
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
        var dirty = (_world.Social?.Dirty.Count ?? 0) != 0;
        foreach (var world in _worlds) dirty |= world.PersistenceDirty.Count != 0 || world.WorldNodeDirty;
        if (!dirty) return false;
        var changes = new List<CharacterSave>();
        foreach (var world in _worlds)
            foreach (var connection in world.PersistenceDirty)
                if (_sessions.TryGetValue(connection, out var session))
                    changes.Add(new(session, world.CaptureCharacter(connection), world.PickupClaims(connection)));
        _checkpointWorlds = CaptureWorldWrites();
        if (changes.Count == 0 && _checkpointWorlds.Count == 0 && (_world.Social?.Dirty.Count ?? 0) == 0) return false;
        _checkpointSocial=CaptureSocialWrite();
        _checkpointChanges = changes;
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
        foreach (var world in _worlds) world.GroundItems?.CommitClaims();
        _checkpoint = null; _checkpointChanges = null;
        _checkpointDeadline!.Dispose(); _checkpointDeadline = null;
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
        if (_regions is not null)
            return ((IRegionalCharacterStore)_characters!).SaveRegionalCheckpointAsync(characters, worlds, social, token);
        return social is null ? _characters!.SaveWithWorldAsync(characters, worlds.SingleOrDefault(), token) :
            ((ISocialStore)_characters!).SaveSocialCheckpointAsync(characters, worlds.SingleOrDefault(), social, token);
    }

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
            if (!_sessions.Remove(connection, out var session)) continue;
            _sessionCredentialHashes.Remove(connection);
            var state = WorldFor(connection).CaptureCharacter(connection);
            RemoveRegionalPlayer(connection);
            _closing.Add(SaveAndCloseAsync(session, state));
        }
        _departed.Clear();
    }

    private async Task SaveAndCloseAsync(CharacterSession session, CharacterState state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await _characters!.SaveAsync(new[] { new CharacterSave(session, state) }, timeout.Token); }
        finally { await session.DisposeAsync(); }
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
                foreach (var world in _worlds) world.GroundItems?.CommitClaims();
                _checkpoint = null;
            }
            var changes = new List<CharacterSave>(_sessions.Count);
            foreach (var (connection, session) in _sessions)
            {
                var world = WorldFor(connection);
                changes.Add(new(session, world.CaptureCharacter(connection), world.PickupClaims(connection)));
            }
            _checkpointWorlds = CaptureWorldWrites();
            _checkpointSocial = CaptureSocialWrite();
            await SaveCheckpointAsync(changes, _checkpointWorlds, _checkpointSocial, timeout.Token);
            CompleteWorldWrite();
            CompleteSocialWrite();
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
                finally { login.Deadline.Dispose(); }
            }
            var cleanup = _sessions.Values.Select(session => session.DisposeAsync().AsTask()).ToList();
            cleanup.AddRange(_closing);
            await Task.WhenAll(cleanup);
            _sessions.Clear(); _logins.Clear();
            await DisposeWorldLeasesAsync();
        }
    }
}
