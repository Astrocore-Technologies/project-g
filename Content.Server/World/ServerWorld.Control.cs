using System.Numerics;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private EntitySnapshot ControlSnapshot(EntitySnapshot snapshot) => Combat?.ControlSnapshot(snapshot) ?? snapshot;
    private NavigationMover? ControlledMover(NetworkEntityId id) => _playersByEntity.TryGetValue(id,out var player)
        ? player.Motion : Npc?.Id == id ? Npc.Motion : Boss?.Id == id ? Boss.Motion : null;
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
}
