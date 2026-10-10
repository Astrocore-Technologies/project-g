using Content.Shared.Network;
namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    // Copy only observable state into the AOI stream, never the private defense resource packet.
    internal AvatarState Avatar(NetworkEntityId id, uint tick)
    {
        var a = _defense[id];
        _swords.TryGetValue(id,out var sword);
        return new(id, tick, HasSword(id), a.GuardUntil > _time, a.Facing,
            (float)Math.Clamp(a.ParryUntil - _time, 0, 60),
            (float)Math.Clamp(_actors[id].StunnedUntil - _time, 0, 60), AvatarGesture.None, 0, 0,
            sword?.FocusUntil > _time && HasSword(id), sword?.RhythmCue ?? 0);
    }
}
