using Content.Shared.Network;
using Godot;
namespace ProjectG.Animation;

/// <summary>Bounded spatial Foley and blade ribbon; all impacts are presentation events.</summary>
public partial class CharacterFeedback : Node3D
{
    private static readonly Dictionary<string, AudioStream> Streams = new();
    private readonly List<AudioStreamPlayer3D> _voices = new();
    private static int _playing;
    private MeshInstance3D _ribbon = null!;
    private readonly ImmediateMesh _mesh = new();
    private readonly StandardMaterial3D _trailMaterial = new() { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency=BaseMaterial3D.TransparencyEnum.Alpha, CullMode=BaseMaterial3D.CullModeEnum.Disabled,
        AlbedoColor=new(.78f,.9f,1,.48f), NoDepthTest=false };
    private readonly Queue<(Vector3 Base, Vector3 Tip)> _history = new();
    private readonly Dictionary<AudioStreamPlayer3D, double> _leases = new();
    private double _clock;
    private MeshInstance3D? _stun;
    public void Pulse(Color color) => AddChild(new ImpactFlash {Position=new(0,1.4f,.1f),Tint=color});
    public void Stun(bool active)
    {
        if(_stun is null && active)
        { _stun=new MeshInstance3D {Mesh=new SphereMesh {Radius=.04f,Height=.08f,RadialSegments=6,Rings=2},MaterialOverride=new StandardMaterial3D {ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,AlbedoColor=new(1,.85f,.3f)}};AddChild(_stun); }
        if(_stun is null)return;
        _stun.Visible=active;_stun.Position=new(Mathf.Cos((float)_clock*8)*.18f,1.94f,Mathf.Sin((float)_clock*8)*.12f);
    }
    public void CancelCue(string name)
    { if(!Streams.TryGetValue(name,out var stream))return;foreach(var voice in _voices) if(voice.Stream==stream)voice.Stop(); }
    public override void _Ready()
    { _ribbon = new MeshInstance3D { Mesh=_mesh, MaterialOverride=_trailMaterial, CastShadow=GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(_ribbon); }
    public void Cue(string name)
    {
        if (_playing >= 40 || GetViewport().GetCamera3D() is { } camera && camera.GlobalPosition.DistanceSquaredTo(GlobalPosition)>1600) return;
        if (!Streams.TryGetValue(name,out var stream))
        {
            var path=$"res://Assets/Audio/Character/{name}.wav";
            if (!ResourceLoader.Exists(path)) return;
            Streams[name]=stream=GD.Load<AudioStream>(path);
        }
        var voice=_voices.FirstOrDefault(v=>!v.Playing && !_leases.ContainsKey(v));
        if (voice is null)
        {
            if (_voices.Count>=3) return;
            voice=new AudioStreamPlayer3D { UnitSize=6, MaxDistance=30, VolumeDb=-10, MaxPolyphony=1 };
            AddChild(voice); _voices.Add(voice);
        }
        voice.Stream=stream; voice.PitchScale=1+(float)GD.RandRange(-.04,.04); voice.Play();
        _leases[voice]=_clock+stream.GetLength()+.3; _playing++;
    }
    public override void _Process(double delta)
    {
        _clock+=delta;
        foreach(var voice in _voices) if (_leases.TryGetValue(voice,out var end) && (_clock>end || !voice.Playing)) { _leases.Remove(voice); _playing--; }
    }
    public void Trail(Skeleton3D skeleton, float progress)
    {
        _mesh.ClearSurfaces();
        if(progress<.24f || progress>.64f) { _history.Clear(); return; }
        var hand=skeleton.FindBone("hand.R"); if(hand<0)return;
        var transform=GlobalTransform.AffineInverse()*skeleton.GlobalTransform*skeleton.GetBoneGlobalPose(hand)*skeleton.GetBoneGlobalRest(hand).AffineInverse();
        _history.Enqueue((transform*new Vector3(-.883f,1.38f,.083f), transform*new Vector3(-.883f,1.38f,.708f)));
        while(_history.Count>5)_history.Dequeue();
        if(_history.Count<2)return;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        var first=true; (Vector3 Base,Vector3 Tip) previous=default;
        foreach(var sample in _history)
        {
            if(!first) { _mesh.SurfaceAddVertex(previous.Base);_mesh.SurfaceAddVertex(previous.Tip);_mesh.SurfaceAddVertex(sample.Tip);
                _mesh.SurfaceAddVertex(previous.Base);_mesh.SurfaceAddVertex(sample.Tip);_mesh.SurfaceAddVertex(sample.Base); }
            previous=sample;first=false;
        }
        _mesh.SurfaceEnd();
    }
    public void Impact(GuardImpact guard)
    {
        Cue(guard==GuardImpact.Parried ? "parry" : guard==GuardImpact.Blocked ? "block" : "hit");
        var effect=new ImpactFlash { Position=new(0,1.1f,.25f), Tint=guard==GuardImpact.Parried ? new(.3f,.85f,1) : guard==GuardImpact.Blocked ? new(1,.75f,.25f) : new(1,.7f,.45f) };
        AddChild(effect);
    }
    public override void _ExitTree() { _playing-=_leases.Count; _leases.Clear(); _mesh.Dispose(); _trailMaterial.Dispose(); }
}
