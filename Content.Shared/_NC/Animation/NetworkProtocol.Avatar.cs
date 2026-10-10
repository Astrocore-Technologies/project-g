using Content.Shared.Combat;
using LiteNetLib.Utils;
namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    // The complete packet is 7 bytes; clients can request only the eight social gestures or cancellation.
    public static bool ValidEmote(EmoteCommand c) => c.Sequence != 0 && c.Gesture is >= AvatarGesture.None and <= AvatarGesture.Sit;
    public static NetDataWriter Write(EmoteCommand c)
    {
        if (!ValidEmote(c)) throw new ArgumentException("Invalid emote.");
        var w = CreateWriter(NetworkMessageType.EmoteCommand); w.Put(c.Sequence); w.Put((byte)c.Gesture); return w;
    }
    public static bool TryReadEmoteCommand(NetDataReader r, out EmoteCommand c)
    {
        c = default;
        if (r.AvailableBytes != 5 || !r.TryGetUInt(out var seq) || !r.TryGetByte(out var gesture)) return false;
        var value = new EmoteCommand(seq, (AvatarGesture)gesture);
        if (!ValidEmote(value)) return false; c = value; return true;
    }
    private static bool ValidAvatar(AvatarState s) => s.EntityId.IsValid && BasicAttackShape.IsValidDirection(s.Facing) &&
        float.IsFinite(s.ParryRemaining) && s.ParryRemaining is >= 0 and <= 60 &&
        float.IsFinite(s.StunRemaining) && s.StunRemaining is >= 0 and <= 60 && Enum.IsDefined(s.Gesture) &&
        float.IsFinite(s.GestureAge) && s.GestureAge is >= 0 and <= 3600;
    public static NetDataWriter Write(AvatarState s)
    {
        if (!ValidAvatar(s)) throw new ArgumentException("Invalid avatar state.");
        var w = CreateWriter(NetworkMessageType.AvatarState); w.Put(s.EntityId.Value); w.Put(s.ServerTick);
        w.Put((byte)((s.Armed ? 1 : 0) | (s.Blocking ? 2 : 0) | (s.FocusReady ? 4 : 0))); WriteVector2(w, s.Facing);
        w.Put(s.ParryRemaining); w.Put(s.StunRemaining); w.Put((byte)s.Gesture); w.Put(s.GestureSequence); w.Put(s.GestureAge); w.Put(s.RhythmCue); return w;
    }
    public static bool TryReadAvatarState(NetDataReader r, out AvatarState s)
    {
        s = default;
        if (r.AvailableBytes != 42 || !r.TryGetULong(out var id) || !r.TryGetUInt(out var tick) || !r.TryGetByte(out var flags) || flags > 7 ||
            !TryReadVector2(r, out var facing) || !r.TryGetFloat(out var parry) || !r.TryGetFloat(out var stun) ||
            !r.TryGetByte(out var gesture) || !r.TryGetUInt(out var seq) || !r.TryGetFloat(out var age) || !r.TryGetUInt(out var rhythm)) return false;
        var value = new AvatarState(new(id), tick, (flags & 1) != 0, (flags & 2) != 0, facing, parry, stun, (AvatarGesture)gesture, seq, age, (flags & 4) != 0, rhythm);
        if (!ValidAvatar(value)) return false; s = value; return true;
    }
}
