using Content.Shared.Network;
using Godot;

namespace ProjectG.Actors;

/// <summary>Client presentation contract. No navigation, damage or server state belongs here.</summary>
public abstract partial class ActorVisual : Node3D
{
    public abstract string CurrentClip { get; }
    public abstract bool Armed { get; }
    public abstract IReadOnlyCollection<string> ClipNames { get; }
    public abstract void Apply(AvatarState state);
    public abstract void SetAlive(bool alive);
    public abstract void Attack(string clip, float normalized, float yaw, bool preparing = false);
    public abstract void EndAttack();
    public abstract void SwingCue(string clip);
    public abstract void ContactCue(string clip);
    public abstract void Hit(Vector3 direction, GuardImpact guard);
    public abstract void PlayGesture(string clip);
    public abstract void Sample(string clip, double time);
    // Snapshots control only the appearance. The actor's authoritative root stays upright.
    public virtual void SetControl(CombatControlPhase phase, double remaining, bool dashing = false) { }
    public virtual void SetAttackPitch(float directionY) { }
    public static string GestureClip(AvatarGesture gesture) => gesture switch
    { AvatarGesture.Wave=>"wave", AvatarGesture.Nod=>"nod", AvatarGesture.No=>"no", AvatarGesture.Bow=>"bow",
      AvatarGesture.Point=>"point", AvatarGesture.Cheer=>"cheer", AvatarGesture.Clap=>"clap", AvatarGesture.Sit=>"sit_down",
      AvatarGesture.Talk=>"talk", AvatarGesture.Pickup=>"pickup", AvatarGesture.Gather=>"gather",
      AvatarGesture.Craft=>"craft", _=>"interact" };
}
