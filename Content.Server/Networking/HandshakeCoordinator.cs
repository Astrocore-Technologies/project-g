using Content.Shared.Network;

namespace Content.Server.Networking;

/// <summary>
/// Keeps unauthenticated connections separate from assigned players.
/// LiteNetLib invokes it from the single polling thread.
/// </summary>
public sealed class HandshakeCoordinator
{
    private readonly Dictionary<int, PlayerId> _connections = new();
    private ulong _nextPlayerId = 1;

    public void RegisterConnection(int connectionId)
    {
        _connections[connectionId] = PlayerId.Invalid;
    }

    public HandshakeDecision ProcessHello(int connectionId, ClientHello hello)
    {
        if (!_connections.TryGetValue(connectionId, out var playerId))
        {
            return HandshakeDecision.Reject(
                HandshakeRejectCode.UnexpectedMessage,
                "Connection is not registered.");
        }

        if (playerId.IsValid)
        {
            return HandshakeDecision.Reject(
                HandshakeRejectCode.HandshakeAlreadyCompleted,
                "Handshake has already completed.");
        }

        if (hello.ProtocolVersion != NetworkConstants.ProtocolVersion)
        {
            return HandshakeDecision.Reject(
                HandshakeRejectCode.UnsupportedProtocol,
                $"Protocol {hello.ProtocolVersion} is not supported; server requires {NetworkConstants.ProtocolVersion}.");
        }

        var assignedPlayerId = new PlayerId(_nextPlayerId++);
        _connections[connectionId] = assignedPlayerId;
        return HandshakeDecision.Accept(assignedPlayerId);
    }

    public bool TryGetPlayerId(int connectionId, out PlayerId playerId)
    {
        return _connections.TryGetValue(connectionId, out playerId) && playerId.IsValid;
    }

    public PlayerId RemoveConnection(int connectionId)
    {
        return _connections.Remove(connectionId, out var playerId)
            ? playerId
            : PlayerId.Invalid;
    }
}

public readonly record struct HandshakeDecision(
    bool IsAccepted,
    PlayerId PlayerId,
    HandshakeRejectCode RejectCode,
    string RejectReason)
{
    public static HandshakeDecision Accept(PlayerId playerId) =>
        new(true, playerId, default, string.Empty);

    public static HandshakeDecision Reject(HandshakeRejectCode code, string reason) =>
        new(false, PlayerId.Invalid, code, reason);
}
