using System.Numerics;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Content.Shared.Combat;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private EntitySnapshot ControlSnapshot(EntitySnapshot snapshot) => Combat?.ControlSnapshot(snapshot) ?? snapshot;
    private NavigationMover? ControlledMover(NetworkEntityId id) => _playersByEntity.TryGetValue(id,out var player)
        ? player.Motion : TryGetNpcActor(id,out var actor) ? actor.Motion : null;
    private void StopControlledMotion(NetworkEntityId id)
    {
        var motion = ControlledMover(id);
        motion?.Reset(motion.Position,motion.Position,motion.Height,motion.Height);
    }
    private bool StartControlledDash(NetworkEntityId id, Vector2 destination, float speed)
    {
        var motion = ControlledMover(id);
        if (motion is null || !StartSurfaceDash(motion,destination,speed)) return false;
        if (_playersByEntity.TryGetValue(id,out var player)) _movingPlayers.Add(player.ConnectionId);
        return true;
    }

    private bool StartFleeRoute(NetworkEntityId id,Vector2 away)
    {
        var motion=ControlledMover(id); if(motion is null) return false;
        // Probe navigable headings in a stable order. Route length is a steering horizon, never a Fear distance cap.
        ReadOnlySpan<float> angles=[0,MathF.PI/4,-MathF.PI/4,MathF.PI/2,-MathF.PI/2];
        foreach(var angle in angles)
        {
            var direction=new Vector2(away.X*MathF.Cos(angle)-away.Y*MathF.Sin(angle),away.X*MathF.Sin(angle)+away.Y*MathF.Cos(angle));
            if(!SurfaceDash.TryDestination(Navigation,motion.Foot,direction,motion.WalkSpeed*.5f,out var destination,out var height) ||
                Vector2.DistanceSquared(destination,motion.Position)<.0001f || !motion.TrySetTarget(destination,height)) continue;
            if(_playersByEntity.TryGetValue(id,out var player)) _movingPlayers.Add(player.ConnectionId);
            return true;
        }
        StopControlledMotion(id); return false;
    }
}
