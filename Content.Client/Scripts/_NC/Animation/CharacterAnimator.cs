using Content.Shared.Network;
using Godot;
namespace ProjectG.Animation;

/// <summary>Reusable in-place skeleton animation. Never changes navigation, collision or damage.</summary>
public partial class CharacterAnimator : Node3D
{
    [Export(PropertyHint.Range, "1,3,1")] public int FaceVariant { get; set; } = 1;
    [Export(PropertyHint.Range, "1,3,1")] public int HairVariant { get; set; } = 1;
    [Export] public bool PreviewArmed { get; set; }
    private AnimationPlayer _player = null!;
    private Skeleton3D _skeleton = null!;
    private MeshInstance3D? _sword;
    private readonly Dictionary<string, StringName> _clips = new();
    private readonly Dictionary<string, double> _lengths = new();
    private Quaternion[] _previous = [];
    private Vector3[] _positions = [];
    private Vector3 _lastPosition;
    private bool _started, _alive = true, _armed, _blocking;
    private string _clip = "", _attack = "", _oneShot = "";
    private double _clock, _clipTime, _transition, _oneUntil, _deathAt, _stunUntil, _parryUntil, _stepAt;
    private float _attackTime, _yaw, _speed;
    private bool _preparing;
    private uint _stateTick, _gestureSequence;
    private bool _hasState;
    private bool _focused;
    private uint _rhythmCue;
    private readonly List<int> _legs = new();
    private readonly Dictionary<int, Quaternion> _legPose = new();
    private AvatarGesture _gesture;
    private double _gestureAt;
    private float _hitWeight;
    private Vector3 _hitTilt;
    private int _chest;
    private CharacterFeedback _feedback = null!;
    public string CurrentClip => _clip;
    public Skeleton3D Skeleton => _skeleton;
    public bool Armed => _armed;
    public IReadOnlyCollection<string> ClipNames => _clips.Keys;

    public override void _Ready()
    {
        // Cache imported nodes once. All modular heads/hair share the same skin and animation library.
        foreach (var node in FindChildren("*", "", true, false))
        {
            if (node is AnimationPlayer ap) _player = ap;
            if (node is Skeleton3D skeleton) _skeleton = skeleton;
            if (node is not MeshInstance3D mesh) continue;
            var name = mesh.Name.ToString();
            if (name.StartsWith("Face_")) mesh.Visible = name.StartsWith($"Face_{FaceVariant:00}_");
            if (name.StartsWith("Hair_")) mesh.Visible = name.StartsWith($"Hair_{HairVariant:00}_");
            if (name == "Sword") _sword = mesh;
        }
        if (_player is null || _skeleton is null) throw new InvalidOperationException("Character requires a skinned animation library.");
        foreach (var key in _player.GetAnimationList())
        {
            var name = key.ToString().Split('|', '/').Last();
            _clips[name] = key; _lengths[name] = _player.GetAnimation(key).Length;
        }
        _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        _previous = new Quaternion[_skeleton.GetBoneCount()]; _positions = new Vector3[_previous.Length];
        _chest = _skeleton.FindBone("chest");
        for(var i=0;i<_skeleton.GetBoneCount();i++)
            if(_skeleton.GetBoneName(i).ToString().StartsWith("thigh.") || _skeleton.GetBoneName(i).ToString().StartsWith("shin.") ||
               _skeleton.GetBoneName(i).ToString().StartsWith("foot.") || _skeleton.GetBoneName(i).ToString().StartsWith("toe.")) _legs.Add(i);
        _feedback = new CharacterFeedback(); AddChild(_feedback);
        _armed = PreviewArmed;
        Sample("idle", 0); SavePose();
    }
    public void Apply(AvatarState state)
    {
        if (_hasState && !Content.Shared.Movement.MovementSimulation.IsSequenceNewer(state.ServerTick, _stateTick)) return;
        if(_hasState && state.RhythmCue != _rhythmCue) { _feedback.Pulse(new(.6f,.85f,.6f)); _feedback.Cue("recover"); }
        if(state.FocusReady && !_focused) _feedback.Pulse(new(.7f,.88f,1));
        _rhythmCue=state.RhythmCue; _focused=state.FocusReady;
        _hasState = true; _stateTick = state.ServerTick;
        if (state.Armed != _armed) { PlayGesture(state.Armed ? "equip" : "unequip"); _feedback.Cue("equip"); }
        _armed = state.Armed;
        if (state.Blocking != _blocking) PlayGesture(state.Blocking ? "block_enter" : "block_exit");
        _blocking = state.Blocking; _stunUntil = _clock + state.StunRemaining;
        if (state.ParryRemaining > 0 && _parryUntil <= _clock) { PlayGesture("parry"); _feedback.Cue("parry_attempt"); }
        _parryUntil = _clock + state.ParryRemaining;
        if (_blocking || state.ParryRemaining > 0) _yaw = Mathf.Atan2(state.Facing.X, state.Facing.Y);
        if (state.GestureSequence != _gestureSequence)
        {
            if (_gesture == AvatarGesture.Sit && state.Gesture == AvatarGesture.None) PlayGesture("stand_up");
            _gesture = state.Gesture; _gestureSequence = state.GestureSequence; _gestureAt = _clock - state.GestureAge;
        }
    }
    public void SetAlive(bool alive)
    {
        if (alive == _alive) return;
        _alive = alive; _attack = _oneShot = ""; _gesture = AvatarGesture.None; _blocking = false;
        _deathAt = _clock; _stunUntil = _parryUntil = 0;
        if (!alive) _feedback.Cue("death");
    }
    public void Attack(string clip, float normalized, float yaw, bool preparing = false)
    {
        if (!_alive) return;
        _attack = clip; _attackTime = normalized; _yaw = yaw; _gesture = AvatarGesture.None; _preparing=preparing;
    }
    public void EndAttack() { _attack = ""; _feedback?.CancelCue("breath"); }
    public void SwingCue(string clip) => _feedback.Cue(clip);
    public void ContactCue(string clip)
    {
        if (clip == "breath") _feedback.Cue("recover");
    }
    public void Hit(Vector3 direction, GuardImpact guard)
    {
        if (!_alive) return;
        if (guard == GuardImpact.Parried) { PlayGesture("parry_success"); _feedback.Impact(guard); return; }
        if (guard == GuardImpact.Blocked) PlayGesture("block_hit");
        else
        {
            _hitWeight = 1; var local = Basis.Inverse() * direction; _hitTilt = new(-local.Z * .2f, 0, local.X * .15f);
            if(_attack.Length==0 && !_blocking) PlayGesture(Math.Abs(local.X)>Math.Abs(local.Z) ? local.X>0 ? "hit_left" : "hit_right" : local.Z>0 ? "hit_back" : "hit_front");
        }
        _feedback.Impact(guard);
    }
    public void PlayGesture(string clip)
    {
        if (!_alive || !_lengths.TryGetValue(clip, out var duration)) return;
        _oneShot = clip; _oneUntil = _clock + duration;
    }
    public override void _Process(double delta)
    {
        _clock += delta;
        var position = GlobalPosition;
        var velocity = _started && delta > 0 ? (position - _lastPosition) / (float)delta : Vector3.Zero;
        velocity.Y = 0; _lastPosition = position; _started = true;
        // Teleports/reconciliation corrections are not a footstep or a sprint.
        var measured = velocity.Length(); if (measured > 35) measured = 0;
        _speed = Mathf.Lerp(_speed, measured, Math.Min(1, (float)delta * 14));
        var clip = _armed ? "combat_idle" : "idle";
        double time = _clock;
        if(!_armed && _clock%18>15) {clip="idle_look";time=_clock%18-15;}
        if (_speed > .08f)
        {
            var running = _speed > 1.7f;
            clip = (running ? "run" : "walk") + (_armed ? "_sword" : "");
            _clipTime += delta * Math.Clamp(_speed / (running ? 2.5 : 1.2), .25, 3);
            time = _clipTime;
            if (!_blocking && _attack.Length == 0) _yaw = Mathf.Atan2(velocity.X, velocity.Z);
            if (_alive && _clock >= _stepAt && _speed < 6)
            { _feedback.Cue("footstep"); _stepAt = _clock + (running ? .325 : .5) * Math.Clamp((running ? 2.5 : 1.2) / _speed, .4, 2); }
        }
        var locomotionClip = clip; var locomotionTime = time;
        if (_gesture != AvatarGesture.None && _speed < .12f)
        {
            time = _clock - _gestureAt;
            clip = GestureClip(_gesture);
            if (_gesture == AvatarGesture.Sit && time >= .8) { clip = "sit_idle"; time -= .8; }
        }
        if (_blocking) clip = "block_hold";
        if (_clock < _oneUntil && _oneShot.Length > 0) { clip = _oneShot; time = _lengths[clip] - (_oneUntil - _clock); }
        if (_attack.Length > 0) { clip = _attack; time = _attackTime * _lengths.GetValueOrDefault(clip, 1); }
        if(_attack.StartsWith("cast"))
        {
            clip=_preparing ? _attackTime<.18f ? "cast_start" : "cast_hold" : _attack;
            time=_preparing ? _attackTime<.18f ? _attackTime/.18f*_lengths[clip] : _clock : Math.Max(0,(_attackTime-.35f)/.65f)*_lengths[clip];
        }
        if (_clock < _stunUntil) { clip = "stun_loop"; time = _clock; }
        if (!_alive) { time = _clock - _deathAt; clip = time < 1.1 ? "death_back" : "dead_back"; }
        Rotation = new(0, Mathf.LerpAngle(Rotation.Y, _yaw, Math.Min(1, (float)delta * 18)), 0);
        if (_sword is not null) _sword.Visible = _armed && _gesture == AvatarGesture.None && !(clip.StartsWith("cast") || clip == "breath");
        if (_clip != clip) _transition = .12;
        var movingUpperBody = _alive && _speed > .12f && _clock >= _stunUntil &&
            (clip.StartsWith("block") || clip.StartsWith("parry") || clip.StartsWith("cast") ||
             _attack.Length > 0 && clip is not "dash" and not "whirl" and not "breath");
        if(movingUpperBody)
        {
            Sample(locomotionClip,locomotionTime);
            foreach(var bone in _legs) _legPose[bone]=_skeleton.GetBonePoseRotation(bone);
        }
        Sample(clip, time);
        if(movingUpperBody) foreach(var bone in _legs) _skeleton.SetBonePoseRotation(bone,_legPose[bone]);
        // Blend the sampled pose on the skeleton, leaving the authoritative root completely untouched.
        if (_transition > 0)
        {
            var t = Math.Min(1, (float)(delta / Math.Max(delta, _transition)));
            for (var i = 0; i < _previous.Length; i++)
            { _skeleton.SetBonePoseRotation(i, _previous[i].Slerp(_skeleton.GetBonePoseRotation(i), t)); _skeleton.SetBonePosePosition(i, _positions[i].Lerp(_skeleton.GetBonePosePosition(i), t)); }
            _transition -= delta;
        }
        SavePose();
        _feedback.Trail(_skeleton, _alive && _armed && _attack.Length > 0 && _attack != "breath" && _attack != "dash" ? _attackTime : 0);
        _feedback.Stun(_alive && _clock < _stunUntil);
        if (_hitWeight > 0 && _chest >= 0)
        { _skeleton.SetBonePoseRotation(_chest, _skeleton.GetBonePoseRotation(_chest) * Quaternion.FromEuler(_hitTilt * _hitWeight)); _hitWeight = Math.Max(0, _hitWeight - (float)delta * 5); }
    }
    public void Sample(string clip, double time)
    {
        if (!_clips.TryGetValue(clip, out var key)) return;
        if (_clip != clip) { _player.Play(key); _player.Pause(); _clip = clip; }
        var length = _lengths[clip];
        var loop = clip is "idle" or "combat_idle" or "walk" or "walk_sword" or "run" or "run_sword" or "stun_loop" or "cast_hold" or "sit_idle" or "block_hold" or "dead_back" or "dead_front";
        _skeleton.ResetBonePoses();
        _player.Seek(loop && length > 0 ? time % length : Math.Clamp(time, 0, length), true);
    }
    private void SavePose()
    { for (var i = 0; i < _previous.Length; i++) { _previous[i] = _skeleton.GetBonePoseRotation(i); _positions[i] = _skeleton.GetBonePosePosition(i); } }
    public static string GestureClip(AvatarGesture g) => g switch
    { AvatarGesture.Wave => "wave", AvatarGesture.Nod => "nod", AvatarGesture.No => "no", AvatarGesture.Bow => "bow", AvatarGesture.Point => "point", AvatarGesture.Cheer => "cheer", AvatarGesture.Clap => "clap", AvatarGesture.Sit => "sit_down", AvatarGesture.Talk => "talk", AvatarGesture.Pickup => "pickup", AvatarGesture.Gather => "gather", AvatarGesture.Craft => "craft", _ => "interact" };
}
