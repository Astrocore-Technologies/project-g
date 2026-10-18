using System.Numerics;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private const float FollowStartDistance = 40, FollowDistance = 3;
    private const double FollowRetrySeconds = 5, FollowRepathSeconds = .25;
    private readonly Dictionary<NetworkEntityId, FollowRoute> _follows = new();
    private readonly Dictionary<NetworkEntityId, HashSet<NetworkEntityId>> _followers = new();
    private readonly Dictionary<int, FollowState> _followStates = new();
    private readonly List<NetworkEntityId> _followStops = new();
    private double _followTime;
    private sealed class FollowRoute(FollowCommand command, Vector3 lastSeen)
    {
        public readonly FollowCommand Command = command;
        public Vector3 LastSeen = lastSeen;
        public Vector3 Planned = new(float.NaN);
        public double NextPlan;
        public double? Deadline;
        public FollowPhase Phase = FollowPhase.Active;
        public bool PathValid = true;
    }

    public FollowState? FollowFor(int connection) => _followStates.TryGetValue(connection, out var state) ? state : null;

    public bool TryStartFollow(int connection, FollowCommand command)
    {
        if (!_playersByConnection.TryGetValue(connection, out var player) || !player.Loaded || command.Sequence == 0 || command.InputOrder == 0 ||
            !MovementSimulation.IsSequenceNewer(command.Sequence, player.LastProcessedSequence)) return false;
        if (player.CancelledNavigationOrder != 0 && !MovementSimulation.IsSequenceNewer(command.InputOrder, player.CancelledNavigationOrder) ||
            player.NavigationOrder != 0 && !MovementSimulation.IsSequenceNewer(command.InputOrder, player.NavigationOrder)) return false;
        var reason = !Alive(player) || Combat?.OrdinaryMovement(player.EntityId) <= 0 || player.Motion.IsDashing || Combat?.IsFleeing(player.EntityId) == true
            ? FollowReason.Restricted : !_playersByEntity.TryGetValue(command.Target, out var target) || command.Target == player.EntityId || !Alive(target)
            ? FollowReason.InvalidTarget : Vector3.DistanceSquared(FollowFoot(player), FollowFoot(target)) > FollowStartDistance * FollowStartDistance
            ? FollowReason.TooFar : !FollowVisible(player, target) ? FollowReason.NotVisible : FollowReason.None;
        if (reason != FollowReason.None)
        {
            _followStates[connection] = new(command.Sequence, Tick, command.InputOrder, default, FollowPhase.Rejected, reason);
            return false;
        }
        EndFollow(player.EntityId, FollowReason.ManualInput, false);
        player.Motion.Reset(player.Position, player.Position, player.Height, player.Height);
        _movingPlayers.Remove(connection);
        player.NavigationOrder = command.InputOrder;
        player.LastProcessedSequence = command.Sequence; player.LastMovementClientTick = command.ClientTick; player.LastMovementServerTick = Tick;
        var route = new FollowRoute(command, _playersByEntity[command.Target].Foot);
        _follows.Add(player.EntityId, route);
        if (!_followers.TryGetValue(command.Target, out var followers)) _followers.Add(command.Target, followers = new());
        followers.Add(player.EntityId);
        PublishFollow(player, route, FollowPhase.Active, FollowReason.None);
        return true;
    }

    private bool Alive(ServerPlayer player) => player.Loaded && player.ConnectionId >= 0 && (Combat?.Get(player.EntityId).Health ?? 1) > 0;
    private Vector3 FollowFoot(ServerPlayer player) => Combat?.Get(player.EntityId).Foot ?? player.Foot;
    private bool FollowVisible(ServerPlayer observer, ServerPlayer target) =>
        Vector2.DistanceSquared(observer.Position, target.Position) <= _interest.Radius * _interest.Radius && CanObserveActor(observer, target.EntityId);

    private void UpdateFollows(float delta)
    {
        _followTime += delta;
        _followStops.Clear();
        foreach (var (id, route) in _follows)
        {
            var player = _playersByEntity[id];
            if (!Alive(player) || !_playersByEntity.TryGetValue(route.Command.Target, out var target) || !Alive(target))
            { _followStops.Add(id); continue; }
            var visible = FollowVisible(player, target);
            var withinRange = Vector3.DistanceSquared(FollowFoot(player), FollowFoot(target)) <= FollowStartDistance * FollowStartDistance;
            if (visible && withinRange) route.LastSeen = target.Foot;
            var direct = visible && withinRange && Navigation.TraverseSurface(player.Foot, route.LastSeen);
            var close = direct && Vector3.DistanceSquared(FollowFoot(player), FollowFoot(target)) <= FollowDistance * FollowDistance;
            var path = route.PathValid;
            // Control pauses ordinary travel; it does not silently replace the Follow intention.
            var canMove = !(Combat?.OrdinaryMovement(id) <= 0) && !player.Motion.IsDashing && Combat?.IsFleeing(id) != true;
            if (canMove && close)
            {
                route.PathValid = path = true;
                player.Motion.Reset(player.Position, player.Position, player.Height, player.Height);
                _movingPlayers.Remove(player.ConnectionId);
            }
            else if (canMove && _followTime >= route.NextPlan)
            {
                route.NextPlan = _followTime + FollowRepathSeconds;
                var goal = route.LastSeen;
                if (direct)
                {
                    var distance = Vector3.Distance(player.Foot, goal);
                    if (distance > FollowDistance) goal = Vector3.Lerp(player.Foot, goal, (distance - FollowDistance) / distance);
                }
                if (Vector3.DistanceSquared(route.Planned, goal) > .04f || !float.IsFinite(route.Planned.X) || !player.Motion.IsMoving)
                {
                    route.PathValid = path = player.Motion.TrySetTarget(new(goal.X, goal.Z), goal.Y);
                    if (path) { route.Planned = goal; if (player.Motion.IsMoving) _movingPlayers.Add(player.ConnectionId); }
                }
            }
            var recovering = !visible || !withinRange || !path;
            if (recovering)
            {
                route.Deadline ??= _followTime + FollowRetrySeconds;
                if (_followTime >= route.Deadline) { _followStops.Add(id); continue; }
                if (route.Phase != FollowPhase.Recovering)
                    PublishFollow(player, route, FollowPhase.Recovering, !visible ? FollowReason.NotVisible : !withinRange ? FollowReason.TooFar : FollowReason.NoPath);
            }
            else
            {
                route.Deadline = null;
                if (route.Phase != FollowPhase.Active) PublishFollow(player, route, FollowPhase.Active, FollowReason.None);
            }
        }
        foreach (var id in _followStops)
        {
            var route = _follows[id]; var player = _playersByEntity[id];
            var reason = !player.Loaded || player.ConnectionId < 0 ? FollowReason.TargetLeft : !Alive(player) ? FollowReason.Died :
                !_playersByEntity.TryGetValue(route.Command.Target, out var target) || target.ConnectionId < 0 || !target.Loaded
                ? FollowReason.TargetLeft : !Alive(target) ? FollowReason.Died : FollowReason.LostTarget;
            EndFollow(id, reason, true);
        }
    }

    private void PublishFollow(ServerPlayer player, FollowRoute route, FollowPhase phase, FollowReason reason)
    {
        route.Phase = phase;
        _followStates[player.ConnectionId] = new(route.Command.Sequence, Tick, route.Command.InputOrder, route.Command.Target, phase, reason);
    }

    private void EndFollow(NetworkEntityId id, FollowReason reason, bool stop)
    {
        if (!_follows.Remove(id, out var route) || !_playersByEntity.TryGetValue(id, out var player)) return;
        if (_followers.TryGetValue(route.Command.Target, out var followers))
        { followers.Remove(id); if (followers.Count == 0) _followers.Remove(route.Command.Target); }
        _followStates[player.ConnectionId] = new(route.Command.Sequence, Tick, route.Command.InputOrder, default, FollowPhase.Stopped, reason);
        if (stop) CancelNavigationForAction(id, route.Command.InputOrder);
    }

    private void RemoveFollowActor(ServerPlayer player)
    {
        EndFollow(player.EntityId, FollowReason.TargetLeft, true);
        if (_followers.TryGetValue(player.EntityId, out var followers))
            foreach (var id in followers.ToArray()) EndFollow(id, FollowReason.TargetLeft, true);
        _followStates.Remove(player.ConnectionId);
    }
}
