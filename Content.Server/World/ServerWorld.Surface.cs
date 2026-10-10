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
    private float LocateHeight(Vector2 point) => Navigation.Surface?.TryLocate(new(point.X, 0, point.Y), .35f, out var located) == true
        ? located.Position.Y : 0;

    // A loading actor is persisted, but cannot act or be targeted until its client applies the baseline.
    public void SetLoaded(int connection, bool loaded)
    {
        var player = _playersByConnection[connection]; player.Loaded = loaded;
        if (Combat is not null) Combat.Get(player.EntityId).Active = loaded || IsCombatTagged(connection);
        if (!loaded) { _movingPlayers.Remove(connection); player.Motion.Reset(player.Position, player.Position); }
    }
}
