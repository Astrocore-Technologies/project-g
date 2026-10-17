using System.Numerics;
namespace Content.Shared.Network;

// Public presentation only. No stamina, profession conditions or hidden counters.
public enum AvatarGesture : byte { None, Wave, Nod, No, Bow, Point, Cheer, Clap, Sit, Talk, Pickup, Gather, Craft, Interact }
public readonly record struct EmoteCommand(uint Sequence, AvatarGesture Gesture);
public readonly record struct AvatarState(NetworkEntityId EntityId, uint ServerTick, bool Armed, bool Blocking,
    Vector2 Facing, float ParryRemaining, float StunRemaining, AvatarGesture Gesture, uint GestureSequence, float GestureAge,
    bool FocusReady = false, uint RhythmCue = 0);
