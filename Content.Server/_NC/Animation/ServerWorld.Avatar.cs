using Content.Shared.Network;
using Content.Shared.Movement;
namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private sealed class GestureState
    {
        public uint RequestSequence, Sequence;
        public double Started, ReadyAt;
        public AvatarGesture Gesture;
        public System.Numerics.Vector2 Position;
    }
    private readonly Dictionary<NetworkEntityId, GestureState> _gestures = new();
    private readonly Dictionary<NetworkEntityId, EmoteCommand> _emotePending = new();
    private readonly HashSet<NetworkEntityId> _activeGestures = new();
    private readonly List<NetworkEntityId> _endedGestures = new();
    private double _avatarTime;

    public bool TryQueueEmote(int connection, EmoteCommand command)
    {
        if (!NetworkProtocol.ValidEmote(command) || !_playersByConnection.TryGetValue(connection, out var p)) return false;
        if (!_gestures.TryGetValue(p.EntityId, out var g)) _gestures[p.EntityId] = g = new();
        if (!MovementSimulation.IsSequenceNewer(command.Sequence, g.RequestSequence)) return false;
        g.RequestSequence = command.Sequence;
        if (_emotePending.ContainsKey(p.EntityId) || command.Gesture != AvatarGesture.None && _avatarTime < g.ReadyAt) return false;
        _emotePending[p.EntityId] = command; return true;
    }
    public AvatarState Avatar(NetworkEntityId id)
    {
        var state = Combat!.Avatar(id, Tick);
        return _gestures.TryGetValue(id, out var g) ? state with { Gesture = g.Gesture, GestureSequence = g.Sequence,
            GestureAge = g.Gesture == AvatarGesture.None ? 0 : (float)Math.Min(3600, _avatarTime - g.Started) } : state;
    }
    private void StartGesture(NetworkEntityId id, AvatarGesture gesture)
    {
        if (!_playersByEntity.TryGetValue(id, out var p)) return;
        if (!_gestures.TryGetValue(id, out var g)) _gestures[id] = g = new();
        g.Gesture = gesture; g.Started = _avatarTime; g.Position = p.Position;
        if (++g.Sequence == 0) ++g.Sequence;
        if (gesture != AvatarGesture.None) _activeGestures.Add(id); else _activeGestures.Remove(id);
    }
    private void SimulateAvatars(float delta)
    {
        _avatarTime += delta;
        foreach (var (id, command) in _emotePending)
        {
            if (!_playersByEntity.TryGetValue(id, out var p) || Combat is null) continue;
            var actor = Combat.Get(id);
            if (command.Gesture != AvatarGesture.None && (actor.Health <= 0 || actor.IsCasting || Combat.IsDefending(id) || Combat.IsStunned(id) || p.Motion.IsMoving)) continue;
            StartGesture(id, command.Gesture); _gestures[id].ReadyAt = _avatarTime + .75;
        }
        _emotePending.Clear(); _endedGestures.Clear();
        // Only actors currently gesturing are visited; idle players are not scanned.
        foreach (var id in _activeGestures)
        {
            var g = _gestures[id];
            if (!_playersByEntity.TryGetValue(id, out var p) || Combat is null || Combat.Get(id).Health <= 0 ||
                Combat.Get(id).IsCasting || Combat.IsDefending(id) || Combat.IsStunned(id) || p.Motion.IsMoving ||
                System.Numerics.Vector2.DistanceSquared(p.Position, g.Position) > .0025f ||
                _avatarTime - g.Started > (g.Gesture == AvatarGesture.Sit ? 3600 : 2.2)) _endedGestures.Add(id);
        }
        foreach (var id in _endedGestures) StartGesture(id, AvatarGesture.None);
        if (Combat is not null) foreach (var hit in Combat.Events)
        {
            if (_activeGestures.Contains(hit.AttackerId)) StartGesture(hit.AttackerId, AvatarGesture.None);
            if (hit.Damage > 0 && _activeGestures.Contains(hit.TargetId)) StartGesture(hit.TargetId, AvatarGesture.None);
        }
        if (Abilities is not null) foreach (var hit in Abilities.Hits)
            if (hit.Damage > 0 && _activeGestures.Contains(hit.TargetId)) StartGesture(hit.TargetId, AvatarGesture.None);
        // These are completed authoritative interactions, not claims supplied by the client.
        if (GroundItems is not null) foreach (var (id, result) in GroundItems.Results)
            if (result.ServerTick == Tick && result.Outcome == PickupOutcome.Accepted) StartGesture(id, AvatarGesture.Pickup);
    }
    private void RemoveAvatar(NetworkEntityId id)
    { _gestures.Remove(id); _emotePending.Remove(id); _activeGestures.Remove(id); }
}
