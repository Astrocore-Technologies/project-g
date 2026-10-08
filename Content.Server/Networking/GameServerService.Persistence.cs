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
    private Task? _checkpoint;
    private CancellationTokenSource? _checkpointDeadline;
    private List<CharacterSave>? _checkpointChanges;

    private sealed record Login(NetPeer Peer, ClientHello Hello, PlayerId Player, Task<CharacterSession> Task,
        CancellationTokenSource Deadline);
    private sealed class Intentions
    {
        public MoveCommand? Move;
        public AttackCommand? Attack;
        public AbilityCommand? Ability;
    }

    private void BeginLogin(NetPeer peer, ClientHello hello, PlayerId player)
    {
        if (!IPAddress.IsLoopback(peer.Address))
        {
            Reject(peer, HandshakeRejectCode.DevelopmentOnly, "Development identity is restricted to localhost.");
            return;
        }
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
            if (intentions.Move is { } move) _world.TryApplyMove(connection, move);
            if (intentions.Attack is { } attack) _world.TryQueueAttack(connection, attack);
            if (intentions.Ability is { } ability) _world.TryQueueAbility(connection, ability, _peers[connection].Ping);
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
        if (_characters is null || _world.PersistenceDirty.Count == 0) return false;
        var changes = new List<CharacterSave>(_world.PersistenceDirty.Count);
        foreach (var connection in _world.PersistenceDirty)
            if (_sessions.TryGetValue(connection, out var session))
                changes.Add(new(session, _world.CaptureCharacter(connection)));
        if (changes.Count == 0) return false;
        _checkpointChanges = changes;
        _checkpointDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Host cancellation must not interrupt an in-flight final checkpoint.
        _checkpoint = _characters.SaveAsync(changes, _checkpointDeadline.Token);
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
        _checkpoint = null; _checkpointChanges = null;
        _checkpointDeadline!.Dispose(); _checkpointDeadline = null;
        BroadcastSnapshot();
    }

    private void CloseDepartedPlayers()
    {
        foreach (var connection in _departed)
        {
            if (!_sessions.Remove(connection, out var session)) continue;
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
                _checkpoint = null;
            }
            var changes = new List<CharacterSave>(_sessions.Count);
            foreach (var (connection, session) in _sessions) changes.Add(new(session, _world.CaptureCharacter(connection)));
            await _characters.SaveAsync(changes, timeout.Token);
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
        }
    }
}
