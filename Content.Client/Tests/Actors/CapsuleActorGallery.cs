using Godot;
namespace ProjectG.Actors;

/// <summary>Authored capsule stand: select a clip, pause, or scrub its normalized time.</summary>
public partial class CapsuleActorGallery : Node3D
{
    private CapsuleActorVisual[] _actors=[];
    private Node3D[] _weapons=[];
    private Node3D? _crowd;
    private OptionButton _selector=null!;
    private HSlider _scrub=null!;
    private Label _status=null!;
    private bool _paused;
    private double _time;
    private string _clip="";
    private readonly Dictionary<string,double> _lengths=new();
    private static readonly string[] Examples=["run_sword","wave","block_hold","talk","cast_hold","npc_strike","boss_slam"];
    public override void _Ready()
    {
        _actors=GetNode("Actors").GetChildren().OfType<CapsuleActorVisual>().ToArray();
        _weapons=_actors.Select(a=>a.GetNode<Node3D>("Center/Pose/Weapon")).ToArray();
        _selector=GetNode<OptionButton>("UI/Panel/Controls/Clips");
        _scrub=GetNode<HSlider>("UI/Panel/Controls/Time");
        _status=GetNode<Label>("UI/Panel/Controls/Status");
        _selector.AddItem("Примеры по ролям");
        var library=_actors[0].GetNode<AnimationPlayer>("AnimationPlayer");
        foreach(var clip in _actors[0].ClipNames.Order()) { _selector.AddItem(clip); _lengths[clip]=library.GetAnimation(clip).Length; }
        foreach(var actor in _actors) actor.SetProcess(false);
        _selector.ItemSelected+=Selected; _scrub.ValueChanged+=Scrubbed;
    }
    public void SetClip(string clip)
    {
        if(clip.Length>0 && !_actors[0].ClipNames.Contains(clip)) throw new ArgumentException(clip);
        _clip=clip; _time=0; _paused=false;
    }
    private void Selected(long index) => SetClip(index==0?"":_selector.GetItemText((int)index));
    private void Scrubbed(double value)
    {
        _paused=true;
        _time=value*_lengths[_clip.Length>0?_clip:"idle"]; ApplySamples();
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if(ev is InputEventKey {Pressed:true,Echo:false,PhysicalKeycode:Key.Space}) _paused=!_paused;
    }
    public override void _Process(double delta)
    {
        if(!_paused) _time+=delta;
        ApplySamples();
        _status.Text=$"{(_clip.Length==0?"Примеры":_clip)} · {(_paused?"Пауза":"Воспроизведение")} · Space: пауза";
    }
    private void ApplySamples()
    {
        for(var i=0;i<_actors.Length;i++)
        {
            var actor=_actors[i]; var clip=_clip.Length>0?_clip:Examples[i%Examples.Length];
            var length=_lengths[clip];
            actor.Sample(clip,_time%length);
            _weapons[i].Visible=actor.Armed && !clip.StartsWith("cast");
        }
        if(!_paused)
        {
            var length=_lengths[_clip.Length>0?_clip:"run_sword"];
            _scrub.SetValueNoSignal(_time%length/length);
        }
    }
    // Test-only crowd uses the production scene; it never replaces authored game placement.
    public void Crowd(int count)
    {
        if(count<0 || count>64) throw new ArgumentOutOfRangeException(nameof(count));
        if(_crowd is not null) { RemoveChild(_crowd); _crowd.QueueFree(); _crowd=null; }
        if(count==0) return;
        _crowd=new Node3D(); AddChild(_crowd);
        for(var i=0;i<count;i++)
        {
            var actor=ActorScenes.Load("ResidentFigure").Instantiate<CapsuleActorVisual>();
            actor.PreviewOnly=true; actor.Position=new((i%8-3.5f)*1.5f,0,(i/8-1.5f)*2);
            _crowd.AddChild(actor);
        }
    }
    public Godot.Collections.Dictionary Metrics()=>new()
    {
        ["renderer"]=ProjectSettings.GetSetting("rendering/renderer/rendering_method"),
        ["fps"]=Engine.GetFramesPerSecond(),
        ["process_ms"]=Performance.GetMonitor(Performance.Monitor.TimeProcess)*1000,
        ["draw_calls"]=Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
        ["objects"]=Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame),
        ["nodes"]=GetTree().GetNodeCount()
    };
    public override void _ExitTree() { _selector.ItemSelected-=Selected; _scrub.ValueChanged-=Scrubbed; }
}