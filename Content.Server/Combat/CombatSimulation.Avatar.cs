using Content.Shared.Network;
namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    // Copy only observable state into the AOI stream, never the private defense resource packet.
    internal AvatarState Avatar(NetworkEntityId id, uint tick)
    {
        var actor=_actors[id];_defense.TryGetValue(id,out var a);
        _swords.TryGetValue(id,out var sword);
        var armed=actor.Kind==CombatEntityKind.Player ? HasSword(id) : actor.Weapon.IsSword;
        return new(id, tick, armed, a?.GuardUntil > _time, actor.Facing,
            (float)Math.Clamp((a?.ParryUntil ?? 0) - _time, 0, 60),
            (float)Math.Clamp(actor.StunnedUntil - _time, 0, 60), AvatarGesture.None, 0, 0,
            sword?.FocusUntil > _time && armed, sword?.RhythmCue ?? 0);
    }
}
