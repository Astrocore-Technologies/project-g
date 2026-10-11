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
}
