using Content.Shared.Movement;
using Content.Shared.Network;
using Godot;
using ProjectG.Animation;

namespace ProjectG.Actors;

/// <summary>Rigid-part, in-place AnimationPlayer presentation, shared by avatars and NPCs.</summary>
public partial class PrimitiveActorVisual : ActorVisual
{
    [Export] public bool PreviewArmed { get; set; }
    [Export] public Color BodyTint { get; set; } = new(.16f,.35f,.52f);
    [Export] public Color HeadTint { get; set; } = new(.85f,.74f,.55f);
    [Export(PropertyHint.Range,"0.5,2,0.05")] public float WidthScale { get; set; } = 1;
    [Export] public bool PreviewOnly { get; set; }
    private AnimationPlayer _player = null!;
    private Node3D _model = null!, _pose = null!, _torso = null!, _weapon = null!, _bladeBase = null!, _bladeTip = null!;
    private CharacterFeedback _feedback = null!;
    private readonly Dictionary<string,StringName> _clips = new();
    private readonly Dictionary<string,double> _lengths = new();
    private readonly List<Node3D> _joints = new();
    private readonly List<Node3D> _legs = new();
    private readonly List<Transform3D> _rest = new(), _previous = new();
    private readonly Dictionary<Node3D,Transform3D> _legPose = new();
    private Vector3 _lastPosition, _hitTilt;
    private bool _started, _alive = true, _armed, _blocking, _preparing, _hasState, _focused, _dashing;
    private string _clip = "", _attack = "", _oneShot = "";
    private float _attackTime, _yaw, _speed, _hitWeight;
    private double _clock, _locomotionTime, _transition, _oneUntil, _deathAt, _stunUntil, _parryUntil, _stepAt;
    private uint _stateTick, _gestureSequence, _rhythmCue;
    private AvatarGesture _gesture;
    private double _gestureAt, _controlRemaining;
    private CombatControlPhase _control;
    // Materials are shared per palette, rather than copied per mesh or frame.
    private static readonly Dictionary<Color,StandardMaterial3D> Materials = new();
    public override string CurrentClip => _clip;
    public override bool Armed => _armed;
    public override IReadOnlyCollection<string> ClipNames => _clips.Keys;
    public Node3D BladeBase => _bladeBase;
    public Node3D BladeTip => _bladeTip;

    public override void _Ready()
    {
        _model = GetNode<Node3D>("Model");
        _model.Scale = new(WidthScale,1,WidthScale);
        foreach(var node in _model.FindChildren("*","",true,false))
        {
            if(node is AnimationPlayer ap) _player = ap;
            if(node is Node3D part && part.Name.ToString() is "Pose" or "Torso" or "Head" or "ArmL" or "ArmR" or
                "ForearmL" or "ForearmR" or "HandL" or "HandR" or "LegL" or "LegR" or "ShinL" or "ShinR")
            {
                _joints.Add(part); _rest.Add(part.Transform); _previous.Add(part.Transform);
                if(part.Name.ToString().StartsWith("Leg") || part.Name.ToString().StartsWith("Shin")) _legs.Add(part);
            }
            if(node is not MeshInstance3D mesh) continue;
            var name=mesh.Name.ToString();
            if(name is "Body" or "Pelvis" || name.StartsWith("UpperArm") || name.StartsWith("LowerArm") || name.StartsWith("Thigh"))
                mesh.MaterialOverride = Palette(BodyTint);
            if(name is "Face" or "Neck" or "Nose" || name.StartsWith("Palm")) mesh.MaterialOverride = Palette(HeadTint);
        }
        _pose = Part("Pose"); _torso = Part("Torso"); _weapon = Part("Weapon");
        _bladeBase = Part("BladeBase"); _bladeTip = Part("BladeTip");
        if(_player is null || _joints.Count != 13) throw new InvalidOperationException("Primitive actor requires 13 rigid joints and AnimationPlayer.");
        foreach(var key in _player.GetAnimationList())
        {
            var name = key.ToString().Split('|','/').Last();
            if(name == "RESET") continue;
            _clips[name] = key; _lengths[name] = _player.GetAnimation(key).Length;
        }
        _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        _feedback = new CharacterFeedback(); AddChild(_feedback);
        _armed = PreviewArmed; _weapon.Visible = _armed;
        Sample("idle",0); SavePose();
    }
    private Node3D Part(string name) => _model.FindChild(name,true,false) as Node3D
        ?? throw new InvalidOperationException("Missing primitive part: "+name);
    private static StandardMaterial3D Palette(Color color)
    {
        if(!Materials.TryGetValue(color,out var material))
            Materials[color] = material = new StandardMaterial3D { AlbedoColor=color, Roughness=.85f,
                DiffuseMode=BaseMaterial3D.DiffuseModeEnum.Toon, SpecularMode=BaseMaterial3D.SpecularModeEnum.Disabled };
        return material;
    }
    public override void Apply(AvatarState state)
    {
        if(_hasState && !MovementSimulation.IsSequenceNewer(state.ServerTick,_stateTick)) return;
        if(_hasState && state.RhythmCue != _rhythmCue) { _feedback.Pulse(new(.6f,.85f,.6f)); Cue("recover"); }
        if(state.FocusReady && !_focused) _feedback.Pulse(new(.7f,.88f,1));
        _rhythmCue=state.RhythmCue; _focused=state.FocusReady; _hasState=true; _stateTick=state.ServerTick;
        if(state.Armed != _armed) { PlayGesture(state.Armed?"equip":"unequip"); Cue("equip"); }
        _armed=state.Armed;
        if(state.Blocking != _blocking) PlayGesture(state.Blocking?"block_enter":"block_exit");
        _blocking=state.Blocking; _stunUntil=_clock+state.StunRemaining;
        if(state.ParryRemaining>0 && _parryUntil<=_clock) { PlayGesture("parry"); Cue("parry_attempt"); }
        _parryUntil=_clock+state.ParryRemaining;
        if(_blocking || state.ParryRemaining>0) _yaw=Mathf.Atan2(state.Facing.X,state.Facing.Y);
        if(state.GestureSequence != _gestureSequence)
        {
            if(_gesture==AvatarGesture.Sit && state.Gesture==AvatarGesture.None) PlayGesture("stand_up");
            _gesture=state.Gesture; _gestureSequence=state.GestureSequence; _gestureAt=_clock-state.GestureAge;
        }
    }
    public override void SetAlive(bool alive)
    {
        if(_alive==alive) return;
        _alive=alive; _attack=_oneShot=""; _gesture=AvatarGesture.None; _blocking=false;
        _deathAt=_clock; _stunUntil=_parryUntil=0; _control=CombatControlPhase.None; _hitWeight=0;
        _dashing=false; _speed=0; _started=false; _feedback.Trail(_bladeBase.GlobalPosition,_bladeTip.GlobalPosition,0);
        if(!alive) Cue("death");
    }
    public override void SetControl(CombatControlPhase phase,double remaining,bool dashing=false)
    {
        _control=phase; _controlRemaining=remaining; _dashing=dashing;
        if(phase!=CombatControlPhase.None) { EndAttack(); _oneShot=""; _gesture=AvatarGesture.None; }
    }
    public override void Attack(string clip,float normalized,float yaw,bool preparing=false)
    {
        if(!_alive || _control!=CombatControlPhase.None) return;
        _attack=clip; _attackTime=Math.Clamp(normalized,0,1); _yaw=yaw; _preparing=preparing; _gesture=AvatarGesture.None;
    }
    public override void EndAttack() { _attack=""; _feedback?.CancelCue("breath"); }
    public override void SwingCue(string clip) => Cue(clip);
    public override void ContactCue(string clip) { if(clip=="breath") Cue("recover"); }
    private void Cue(string clip) { if(!PreviewOnly) _feedback.Cue(clip); }
    public override void Hit(Vector3 direction,GuardImpact guard)
    {
        if(!_alive) return;
        if(guard==GuardImpact.Parried) PlayGesture("parry_success");
        else if(guard==GuardImpact.Blocked) PlayGesture("block_hit");
        else
        {
            var local=Basis.Inverse()*direction;
            _hitWeight=1; _hitTilt=new(-local.Z*.2f,0,local.X*.15f);
            if(_attack.Length==0 && !_blocking)
                PlayGesture(Math.Abs(local.X)>Math.Abs(local.Z)?local.X>0?"hit_left":"hit_right":local.Z>0?"hit_back":"hit_front");
        }
        if(!PreviewOnly) _feedback.Impact(guard);
    }
    public override void PlayGesture(string clip)
    {
        if(!_alive || !_lengths.TryGetValue(clip,out var length)) return;
        _oneShot=clip; _oneUntil=_clock+length;
    }
    public override void _Process(double delta)
    {
        _clock+=delta;
        var position=GlobalPosition;
        var velocity=_started && delta>0?(position-_lastPosition)/(float)delta:Vector3.Zero;
        velocity.Y=0; _lastPosition=position; _started=true;
        var measured=velocity.Length(); if(measured>35) measured=0;
        _speed=Mathf.Lerp(_speed,measured,Math.Min(1,(float)delta*14));
        var clip=_armed?"combat_idle":"idle"; double time=_clock;
        if(!_armed && _clock%18>15) { clip="idle_look"; time=_clock%18-15; }
        if(_speed>.08f)
        {
            var running=_speed>1.7f;
            clip=(running?"run":"walk")+(_armed?"_sword":"");
            _locomotionTime+=delta*Math.Clamp(_speed/(running?2.5:1.2),.25,3); time=_locomotionTime;
            if(!_blocking && _attack.Length==0 && _control==CombatControlPhase.None) _yaw=Mathf.Atan2(velocity.X,velocity.Z);
            if(_alive && _clock>=_stepAt && _speed<6 && _control==CombatControlPhase.None)
            { Cue("footstep"); _stepAt=_clock+(running?.325:.5)*Math.Clamp((running?2.5:1.2)/_speed,.4,2); }
        }
        var locomotion=clip; var locomotionTime=time;
        if(_gesture!=AvatarGesture.None && _speed<.12f)
        {
            time=_clock-_gestureAt; clip=CharacterAnimator.GestureClip(_gesture);
            if(_gesture==AvatarGesture.Sit && time>=.8) { clip="sit_idle"; time-=.8; }
        }
        if(_blocking) clip="block_hold";
        if(_clock<_oneUntil && _oneShot.Length>0) { clip=_oneShot; time=_lengths[clip]-(_oneUntil-_clock); }
        if(_dashing) { clip="dash"; time=.4; }
        if(_attack.Length>0)
        {
            clip=_attack; time=_attackTime*_lengths.GetValueOrDefault(clip,1);
            if(_attack.StartsWith("cast"))
            {
                clip=_preparing?_attackTime<.18f?"cast_start":"cast_hold":_attack;
                time=_preparing?_attackTime<.18f?_attackTime/.18f*_lengths[clip]:_clock:Math.Max(0,(_attackTime-.35f)/.65f)*_lengths[clip];
            }
        }
        if(_clock<_stunUntil) { clip="stun_loop"; time=_clock; }
        if(_control!=CombatControlPhase.None)
        {
            clip=_control switch { CombatControlPhase.Airborne=>"airborne",CombatControlPhase.KnockedDown=>"down_hold",
                CombatControlPhase.Recovering=>"recover",_=>"hit_front" };
            time=_control==CombatControlPhase.Recovering?Math.Max(0,_lengths["recover"]-_controlRemaining):_clock;
        }
        if(!_alive) { time=_clock-_deathAt; clip=time<1.1?"death_back":"dead_back"; }
        Rotation=new(0,Mathf.LerpAngle(Rotation.Y,_yaw,Math.Min(1,(float)delta*18)),0);
        _weapon.Visible=_armed && _gesture==AvatarGesture.None && !clip.StartsWith("cast") && clip!="breath";
        if(_clip!=clip) _transition=.12;
        var mixLegs=_alive && _speed>.12f && _control==CombatControlPhase.None && _clock>=_stunUntil &&
            (clip.StartsWith("block") || clip.StartsWith("parry") || clip.StartsWith("cast") || _attack.Length>0 && clip is not "dash" and not "whirl" and not "breath");
        if(mixLegs) { Sample(locomotion,locomotionTime); foreach(var leg in _legs) _legPose[leg]=leg.Transform; }
        Sample(clip,time);
        if(mixLegs) foreach(var leg in _legs) leg.Transform=_legPose[leg];
        if(_transition>0)
        {
            var amount=Math.Min(1,(float)(delta/Math.Max(delta,_transition)));
            for(var i=0;i<_joints.Count;i++) _joints[i].Transform=_previous[i].InterpolateWith(_joints[i].Transform,amount);
            _transition-=delta;
        }
        SavePose();
        if(_hitWeight>0) { _torso.RotateObjectLocal(Vector3.Right,_hitTilt.X*_hitWeight); _torso.RotateObjectLocal(Vector3.Back,_hitTilt.Z*_hitWeight); _hitWeight=Math.Max(0,_hitWeight-(float)delta*5); }
        _feedback.Trail(_bladeBase.GlobalPosition,_bladeTip.GlobalPosition,
            _alive && _armed && _control==CombatControlPhase.None && _attack.Length>0 && _attack is not "breath" and not "dash" && !_attack.StartsWith("cast")?_attackTime:0);
        _feedback.Stun(_alive && _clock<_stunUntil);
    }
    public override void Sample(string clip,double time)
    {
        if(!_clips.TryGetValue(clip,out var key)) throw new InvalidOperationException("Missing primitive animation: "+clip);
        if(_clip!=clip) { _player.Play(key); _player.Pause(); _clip=clip; }
        for(var i=0;i<_joints.Count;i++) _joints[i].Transform=_rest[i];
        var length=_lengths[clip];
        var loop=clip is "idle" or "combat_idle" or "walk" or "walk_sword" or "run" or "run_sword" or "stun_loop" or
            "cast_hold" or "sit_idle" or "block_hold" or "dead_back" or "dead_front" or "down_hold" or "airborne";
        _player.Seek(loop && length>0?time%length:Math.Clamp(time,0,length),true);
    }
    private void SavePose() { for(var i=0;i<_joints.Count;i++) _previous[i]=_joints[i].Transform; }
}
