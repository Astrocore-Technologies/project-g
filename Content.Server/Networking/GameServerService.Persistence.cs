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
    private WorldNodeSession? _worldSession;
    private WorldNodeSave? _checkpointWorld;
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
            _characters!.OpenAsync(hello.DevelopmentToken, _world.CreateInitialCharacter(), deadline.Token), deadline));
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
            if(intentions.Social is {} social)_world.TryQueueSocial(connection,social);
            if(intentions.Pvp is {} pvp)_world.TryQueuePvp(connection,pvp);
            if(intentions.Economy is { } economy) _world.TryQueueEconomy(connection,economy);
            if(intentions.Trade is { } trade) _world.TryQueueTrade(connection,trade);
            if(intentions.Repair is { } repair) _world.TryQueueRepair(connection,repair);
            if (intentions.Craft is { } craft) _world.TryQueueCraft(connection,craft);
            if (intentions.Node is { } node) _world.TryQueueWorldNode(connection,node);
            if (intentions.NodeSecond is { } nodeSecond) _world.TryQueueWorldNode(connection,nodeSecond);
            if (intentions.Move is { } move) _world.TryApplyMove(connection, move);
            if (intentions.Attack is { } attack) _world.TryQueueAttack(connection, attack);
            if (intentions.Ability is { } ability) _world.TryQueueAbility(connection, ability, _peers[connection].Ping);
            if (intentions.Inventory is { } inventory) _world.TryQueueInventory(connection, inventory);
            if (intentions.Pickup is { } pickup) _world.TryQueuePickup(connection, pickup);
            if (intentions.Profession is { } profession) _world.TryQueueProfession(connection,profession);
            if (intentions.ProfessionSecond is { } professionSecond) _world.TryQueueProfession(connection,professionSecond);
            if (intentions.Progression is { } progression) _world.TryQueueProgression(connection,progression);
            if (intentions.ProgressionSecond is { } second) _world.TryQueueProgression(connection,second);
            if (intentions.Echo is { } echo) _world.TryQueueEchoSignature(connection,echo);
            if (intentions.Revive is { } revive && CanDevelopmentRevive(_peers[connection]))
                _world.TryQueueDevelopmentRevive(connection, revive, authorized: true);
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
        if (_characters is null || (_world.PersistenceDirty.Count == 0 && !_world.WorldNodeDirty && (_world.Social?.Dirty.Count ?? 0)==0)) return false;
        var changes = new List<CharacterSave>(_world.PersistenceDirty.Count);
        foreach (var connection in _world.PersistenceDirty)
            if (_sessions.TryGetValue(connection, out var session))
                changes.Add(new(session, _world.CaptureCharacter(connection), _world.PickupClaims(connection)));
        if (changes.Count == 0 && !_world.WorldNodeDirty && (_world.Social?.Dirty.Count ?? 0)==0) return false;
        _checkpointWorld=CaptureWorldWrite();
        _checkpointSocial=CaptureSocialWrite();
        _checkpointChanges = changes;
        _checkpointDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Host cancellation must not interrupt an in-flight final checkpoint.
        // A synchronous adapter failure must also leave a failed barrier, never trigger an unfenced final save.
        try { _checkpoint = _checkpointSocial is null ? _characters.SaveWithWorldAsync(changes,_checkpointWorld, _checkpointDeadline.Token) : ((ISocialStore)_characters).SaveSocialCheckpointAsync(changes,_checkpointWorld,_checkpointSocial,_checkpointDeadline.Token); }
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
        _world.GroundItems?.CommitClaims();
        _checkpoint = null; _checkpointChanges = null;
        _checkpointDeadline!.Dispose(); _checkpointDeadline = null;
        BroadcastSnapshot();
    }

    private SocialSave? CaptureSocialWrite() => (_world.Social?.Dirty.Count ?? 0)>0 ? new(_socialSession ?? throw new InvalidOperationException("Missing social lease."),_world.Social!.Dirty.ToArray()) : null;
    private void CompleteSocialWrite()
    { if(_checkpointSocial is null)return; _checkpointSocial.Session.Revision++; _world.Social!.Commit(); _checkpointSocial=null; }
    private WorldNodeSave? CaptureWorldWrite() => _world.WorldNodeDirty ? new(_worldSession ?? throw new InvalidOperationException("Missing world owner."),_world.CaptureWorldNode(),_world.WorldNodeAudit.ToArray()) : null;
    private void CompleteWorldWrite()
    {
        if(_checkpointWorld is null) return;
        _checkpointWorld.Session.Revision++; _world.CommitWorldNode(_checkpointWorld.Session.Revision); _checkpointWorld=null;
    }
    private void CloseDepartedPlayers()
    {
        ExpireDetachedCombatants();
        foreach (var connection in _departed)
        {
            if (!_sessions.Remove(connection, out var session)) continue;
            _sessionCredentialHashes.Remove(connection);
            var state = _world.CaptureCharacter(connection);
            _world.RemovePlayer(connection);
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
            if (_checkpoint is not null)
            {
                await _checkpoint;
                foreach (var change in _checkpointChanges!) change.Session.Revision++;
                CompleteWorldWrite();
                CompleteSocialWrite();
                _world.GroundItems?.CommitClaims();
                _checkpoint = null;
            }
            var changes = new List<CharacterSave>(_sessions.Count);
            foreach (var (connection, session) in _sessions)
                changes.Add(new(session, _world.CaptureCharacter(connection), _world.PickupClaims(connection)));
            var worldWrite=CaptureWorldWrite();
            var socialWrite=CaptureSocialWrite();
            if(socialWrite is null) await _characters.SaveWithWorldAsync(changes,worldWrite,timeout.Token);
            else { await ((ISocialStore)_characters).SaveSocialCheckpointAsync(changes,worldWrite,socialWrite,timeout.Token); socialWrite.Session.Revision++; _world.Social!.Commit(); }
            if(worldWrite is not null) { worldWrite.Session.Revision++; _world.CommitWorldNode(worldWrite.Session.Revision); }
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
            if(_socialSession is not null) { await _socialSession.DisposeAsync(); _socialSession=null; }
            if(_worldSession is not null) { await _worldSession.DisposeAsync(); _worldSession=null; }
        }
    }
}
