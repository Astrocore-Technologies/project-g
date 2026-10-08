using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    internal bool ReadyForRegionTravel(int connection)
    {
        var player = _playersByConnection[connection]; var actor = Combat!.Get(player.EntityId);
        // Wait for transient actions rather than discarding projectiles, casts or item reservations.
        return actor.Health > 0 && !_starterPending.Contains(player.EntityId) && !actor.IsCasting && !player.Motion.IsDashing &&
            !Abilities!.HasActiveEffects(player.EntityId) && Inventory?.IsTrading(player.EntityId) != true;
    }

    internal void AuditTravel(Guid character, string source, string destination, bool arrival) =>
        Audit(character.ToString("N"), arrival ? "RegionArrival" : "RegionDeparture", source + ">" + destination);
}
