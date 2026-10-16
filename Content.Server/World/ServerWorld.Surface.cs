using System.Numerics;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private bool StartSurfaceDash(Content.Shared.Navigation.NavigationMover motion, Vector2 destination, float speed)
    {
        if (Navigation.Surface is null) return motion.TryStartDash(destination, speed);
        var points = new List<Content.Shared.Navigation.SurfaceLocation>();
        Navigation.Surface.SampleColumn(destination.X, destination.Y, points);
        foreach (var point in points)
            if (Navigation.TraverseSurface(motion.Foot, point.Position)) return motion.TryStartDash(destination, speed, point.Position.Y);
        return false;
    }
    private Vector3 GroundFoot(Vector2 point) => new(point.X, LocateHeight(point), point.Y);
    private float LocateHeight(Vector2 point)
    {
        if (Navigation.Surface is not { } surface) return 0;
        var column = new List<Content.Shared.Navigation.SurfaceLocation>();
        surface.SampleColumn(point.X, point.Y, column);
        if (column.Count == 0) throw new InvalidOperationException("Configured point is outside the walkable surface.");
        // Legacy two-dimensional content anchors refer to the lowest walkable
        // floor. Explicit player spawns use their authored height below.
        var height = column[0].Position.Y;
        for (var i = 1; i < column.Count; i++) height = MathF.Min(height, column[i].Position.Y);
        return height;
    }

    private bool TryResolveSpawn(Vector2 requested, out Vector2 spawn, out float height)
    {
        height = 0;
        if (Navigation.Surface is not { } surface) return Navigation.TryFindSpawn(requested, out spawn);
        spawn = requested;
        if (!surface.TryLocate(new(requested.X, _spawnHeight, requested.Y), .35f, out var located))
        {
            // Offset slots may hit an obstacle; use the authored spawn without
            // silently selecting another floor or scanning the entire region.
            spawn = _spawn;
            if (!surface.TryLocate(new(_spawn.X, _spawnHeight, _spawn.Y), .35f, out located)) return false;
        }
        height = located.Position.Y;
        return true;
    }

    // A loading actor is persisted, but cannot act or be targeted until its client applies the baseline.
    public void SetLoaded(int connection, bool loaded)
    {
        var player = _playersByConnection[connection]; player.Loaded = loaded;
        var retainedBody=Combat?.Get(player.EntityId).DetachedBody==true;
        if (Combat is not null)
        {
            var actor=Combat.Get(player.EntityId);actor.Active=loaded || retainedBody || IsCombatTagged(connection);
            if(loaded)actor.DetachedBody=false;
            Combat.RefreshHealthRecovery(player.EntityId);
        }
        if (!loaded && !retainedBody) { _movingPlayers.Remove(connection); player.Motion.Reset(player.Position, player.Position, player.Height, player.Height); }
    }
}
