using Content.Shared.Network;
using Content.Shared.Movement;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace ProjectG.Combat;

/// <summary>Visual-only, replaceable AnimationPlayer rig. Never moves the collision body or causes damage.</summary>
public partial class SwordAttackAnimation : Node3D
{
    [Export] public NodePath BodyPath { get; set; } = new("../MeshInstance3D");
    [Export(PropertyHint.Range, "0.1,0.8")] public float ContactTime { get; set; } = .35f;
    [Export(PropertyHint.Range, "0.1,1")] public float RecoverySeconds { get; set; } = .32f;
    private AnimationPlayer _animation = null!;
    private Node3D _body = null!, _pose = null!, _weapon = null!;
    private Transform3D _rest;
    private uint _sequence;
    private ulong _effect;
    private bool _ability, _waiting, _predicted, _active;
    private double _elapsed, _duration, _age;
    private float _from;
    // NC: production actors sample the shared skinned library; the standalone legacy test rig stays usable.
    private ProjectG.Animation.CharacterAnimator? _character;
    private bool _sounded;
    public void Bind(ProjectG.Animation.CharacterAnimator character) { _character=character; }
    public bool IsAnimating => _active;
    public StringName Clip { get; private set; } = "";
    public float ClipPosition { get; private set; }

    public override void _Ready()
    {
        _animation = GetNode<AnimationPlayer>("AnimationPlayer");
        _pose = GetNode<Node3D>("BodyPose"); _weapon = GetNode<Node3D>("SwordPivot");
        _body = GetNode<Node3D>(BodyPath); _rest = _body.Transform;
        Visible = false; SetProcess(false);
    }

    public void PredictBasic(uint sequence, NumericsVector2 direction, double interval)
    {
        // An automatic basic request rejected during a skill must not interrupt that skill's pose.
        if (_active && _ability) return;
        Begin(sequence % 2 == 0 ? "basic_left" : "basic_right", sequence, false, direction, Math.Min(.12, interval * .3), true);
    }

    public void ConfirmBasic(AttackEvent hit)
    {
        // A delayed basic acknowledgement may arrive after the next skill was locally started.
        if (_active && _ability && _waiting) return;
        if (!_active || _ability || _sequence != hit.Sequence)
            Begin(hit.Sequence % 2 == 0 ? "basic_left" : "basic_right", hit.Sequence, false, hit.Direction, 0, false);
        Contact(); // Confirm the predicted swing once; don't restart its wind-up.
    }

    public void RejectBasic(uint sequence)
    { if (_active && !_ability && _sequence == sequence && _predicted) Stop(); }

    public void PredictAbility(uint sequence, ushort ability, NumericsVector2 direction, double seconds)
    {
        var clip = AbilityClip(ability); if (clip.Length == 0) return;
        Begin(clip, sequence, true, direction, seconds, true);
        _weapon.Visible = ability != 3;
    }

    public void ApplyAbility(AbilityEffectState state)
    {
        var clip = AbilityClip(state.AbilityId); if (clip.Length == 0) return;
        if (_active && _ability && MovementSimulation.IsSequenceNewer(_sequence, state.Sequence)) return;
        var same = _active && _ability && _sequence == state.Sequence && (_effect == 0 || _effect == state.EffectId);
        if (state.Phase == AbilityPhase.Finished)
        {
            // Finished before contact means an interrupted cast (or AOI exit), not a successful swing.
            if (same && _waiting) Stop();
            return;
        }
        if (!same)
        {
            Begin(clip, state.Sequence, true, state.Direction, state.RemainingSeconds, false);
            _weapon.Visible = state.AbilityId != 3;
        }
        _effect = state.EffectId; _predicted = false;
        if (state.Phase == AbilityPhase.Impact) Contact();
        else if (_waiting)
        {
            // Keep the current pose when the server replaces a prediction; no duplicate wind-up.
            _from = Math.Min(ClipPosition, ContactTime); _elapsed = 0; _duration = Math.Max(.001, state.RemainingSeconds);
        }
    }

    public void RejectAbility(uint sequence)
    { if (_active && _ability && _sequence == sequence && _predicted) Stop(); }

    private void Begin(string clip, uint sequence, bool ability, NumericsVector2 direction, double windup, bool predicted)
    {
        Stop();
        _sequence = sequence; _ability = ability; _predicted = predicted; _waiting = true; _active = true;
        _sounded = false;
        if(clip=="breath" && _character is not null) { _character.SwingCue(clip); _sounded=true; }
        _elapsed = _age = 0; _duration = Math.Max(.001, windup); _from = 0; _effect = 0;
        Rotation = new(0, Mathf.Atan2(direction.X, direction.Y), 0);
        Clip = clip;
        if (_animation.HasAnimation(Clip)) { _animation.Play(Clip); _animation.Pause(); }
        _weapon.Visible = _character is null; Visible = true; SetProcess(true); Sample(0);
    }

    private void Contact()
    {
        if (!_waiting) return;
        _waiting = false; _predicted = false; _elapsed = 0; _duration = RecoverySeconds;
        _from = ContactTime; Sample(ContactTime);
        _character?.ContactCue(Clip.ToString());
    }

    public override void _Process(double delta)
    {
        _age += delta; _elapsed += delta;
        // Missing confirmations or lost lifecycle messages cannot leave a stuck pose.
        if ((_predicted && _age > 2) || _age > 12) { Stop(); return; }
        var t = (float)Math.Clamp(_elapsed / _duration, 0, 1);
        Sample(Mathf.Lerp(_from, _waiting ? ContactTime : 1, t));
        if (!_waiting && t >= 1) Stop();
    }

    private void Sample(float time)
    {
        ClipPosition = time;
        if (_character is not null)
        {
            _weapon.Visible = false;
            _character.Attack(Clip.ToString(), time, Rotation.Y, _waiting);
            if (!_sounded && time >= .24f) { _sounded = true; _character.SwingCue(Clip.ToString()); }
            return;
        }
        _animation.Seek(time, update: true);
        // Only the render mesh receives the pose; the player, camera and navigation remain untouched.
        _body.Transform = new Transform3D(Basis * _pose.Basis * _rest.Basis, _rest.Origin + Basis * _pose.Position);
    }

    public void Stop()
    {
        _active = false; Visible = false; SetProcess(false);
        _character?.EndAttack();
        if (_animation is null) return;
        _animation.Stop(); _body.Transform = _rest;
    }

    internal static string AbilityClip(ushort id) => id switch
    {
        3 => "dash", 20 => "thrust", 21 => "sweep", 22 => "rend", 23 => "breaker",
        24 => "pommel", 25 => "hamstring", 26 => "riposte", 27 => "whirl",
        28 => "breath", 29 => "finisher", 1 or 2 => "cast_release", 4 or 5 or 6 => "cast_self", _ => ""
    };
}
