using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.Gameplay;
namespace ProjectG.StarterZone;

/// <summary>Public scenery and owner-only exploration presentation; never grants progress.</summary>
public partial class StarterZonePresentation : Node3D
{
    private NetworkClient _network=null!;
    private PlayerController _player=null!;
    private StarterZoneState _zone;
    private ExplorationState? _exploration;
    private StarterMap _map=null!;
    private Label _location=null!;
    private Label _guideText=null!;
    private PanelContainer _guide=null!;
    private PanelContainer _mapPanel=null!;
    private Node3D _bridge=null!;
    private double _refresh;
    private readonly HashSet<ushort> _places=new();
    private static readonly string[] Steps=["ПКМ — пройти по дороге", "ЛКМ — ударить тренировочную цель", "QWER / ASDF — умения; Space — рывок", "T / Y / U — ручная способность Эхо", "Исследовать окрестности", "H у хранителя — помочь переправе", "P — проверить доступные профессии"];
    public void Initialize(NetworkClient network, PlayerController player, StarterZoneState zone)
    {
        _network=network; _player=player; _zone=zone;
        _network.ExplorationReceived+=Apply; _network.WorldNodeReceived+=WorldChanged;
        var canvas=new CanvasLayer { Layer=4 }; AddChild(canvas);
        _mapPanel=new PanelContainer { AnchorLeft=1,AnchorRight=1,AnchorTop=1,AnchorBottom=1,OffsetLeft=-238,OffsetRight=-12,OffsetTop=-330,OffsetBottom=-12,MouseFilter=Control.MouseFilterEnum.Ignore };
        canvas.AddChild(_mapPanel);
        var column=new VBoxContainer { MouseFilter=Control.MouseFilterEnum.Ignore }; _mapPanel.AddChild(column);
        _location=new Label { Text=zone.RegionName+"\nС ↑  В →  Ю ↓  З ←",MouseFilter=Control.MouseFilterEnum.Ignore }; column.AddChild(_location);
        _map=new StarterMap { CustomMinimumSize=new(220,220),MouseFilter=Control.MouseFilterEnum.Ignore }; column.AddChild(_map);
        column.AddChild(new Label { Text="M — карта   V — PvP/смерть; F1 — обучение",MouseFilter=Control.MouseFilterEnum.Ignore });
        _guide=new PanelContainer { AnchorLeft=0.5f,AnchorRight=0.5f,OffsetLeft=-260,OffsetRight=260,OffsetTop=100,MouseFilter=Control.MouseFilterEnum.Stop,Visible=false }; canvas.AddChild(_guide);
        var guideColumn=new VBoxContainer(); _guide.AddChild(guideColumn);
        _guideText=new Label { AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new(500,0) }; guideColumn.AddChild(_guideText);
        var close=new Button { Text="Продолжить путь" }; close.Pressed+=()=>_guide.Hide(); guideColumn.AddChild(close);
        RefreshGuide(); BuildTown();
        if(network.LatestWorldNode is { } state) WorldChanged(state);
    }
    private void RefreshGuide()
    {
        var text=_zone.GuideName+"\nДобро пожаловать в "+_zone.TownName+"!\n\n";
        var flags=_exploration?.Tutorial ?? 0;
        for(var i=0;i<Steps.Length;i++) text+=((flags&(1<<i))!=0 ? "✓ " : "○ ")+Steps[i]+"\n";
        _guideText.Text=text+"\nI — вещи, K — характеристики и навыки, C — добыча, крафт и ремонт.\nB — обмен, J — кузница, монеты и местный рынок.\nИсследуй мир и пробуй разные действия: новые возможности\nпоявляются по мере твоего пути. Подсказки не дают наград.\nV — PvP/смерть; F1 — открыть или закрыть эту памятку.";
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if(input is not InputEventKey { Pressed:true,Echo:false } key) return;
        if(key.Keycode==Key.F1) { _guide.Visible=!_guide.Visible; GetViewport().SetInputAsHandled(); }
        if(key.Keycode==Key.M)
        {
            var large=_map.CustomMinimumSize.X<300;
            _map.CustomMinimumSize=large ? new(380,380) : new(220,220);
            _mapPanel.OffsetLeft=large ? -398 : -238; _mapPanel.OffsetTop=large ? -490 : -330; GetViewport().SetInputAsHandled();
        }
    }
    public override void _Process(double delta)
    {
        _refresh+=delta; if(_refresh<0.25 || _network.Navigation is null) return; _refresh=0;
        var position=_player.PredictedPosition;
        _location.Text=$"{_zone.RegionName}\nX {position.X:0.0}   Z {position.Y:0.0}\nС ↑  В →  Ю ↓  З ←";
        _map.Update(_network,_zone,_exploration,position);
    }
    private void Apply(ExplorationState state)
    {
        if(state.OwnerId!=_player.EntityId) return;
        _exploration=state; RefreshGuide();
        foreach(var place in state.Places)
        {
            if(!_places.Add(place.Id)) continue;
            var marker=new Node3D { Position=new(place.Position.X,0,place.Position.Y) }; AddChild(marker);
            marker.AddChild(new MeshInstance3D { Position=new(0,0.08f,0),Mesh=new CylinderMesh { TopRadius=1.2f,BottomRadius=1.2f,Height=0.05f },MaterialOverride=Material(new(0.16f,0.6f,0.45f)) });
            marker.AddChild(new Label3D { Position=new(0,1.6f,0),Text=place.Name,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled });
        }
    }
    private static StandardMaterial3D Material(Color color) => new() { AlbedoColor=color,Roughness=0.9f };
    private void Box(Vector3 position, Vector3 size, Color color)
    { AddChild(new MeshInstance3D { Position=position,Mesh=new BoxMesh { Size=size },MaterialOverride=Material(color) }); }
    private void BuildTown()
    {
        // Walkable plaza/road decals; buildings stand beyond the existing western region boundary.
        Box(new(-10,0.014f,0),new(8,0.025f,10),new(0.32f,0.29f,0.23f));
        Box(new(-7,0.032f,0),new(14,0.025f,1.8f),new(0.44f,0.38f,0.27f));
        Box(new(-5,0.018f,-5),new(18,0.02f,1.4f),new(0.37f,0.34f,0.26f));
        for(var i=0;i<3;i++)
        {
            var z=-7+i*6; Box(new(-17,1.5f,z),new(3,3,4),new(0.52f,0.43f,0.31f));
            Box(new(-17,3.15f,z),new(3.4f,0.4f,4.4f),new(0.29f,0.18f,0.13f));
        }
        var guide=new Node3D { Position=new(_zone.GuidePosition.X,0,_zone.GuidePosition.Y) }; AddChild(guide);
        guide.AddChild(new MeshInstance3D { Position=new(0,0.8f,0),Mesh=new CapsuleMesh { Radius=0.3f,Height=1.6f },MaterialOverride=Material(new(0.25f,0.5f,0.85f)) });
        guide.AddChild(new Label3D { Position=new(0,2.1f,0),Text=_zone.GuideName+" • F1",Billboard=BaseMaterial3D.BillboardModeEnum.Enabled });
        AddChild(new Label3D { Position=new(_zone.TownPosition.X,2.8f,_zone.TownPosition.Y-4),Text=_zone.TownName,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled });
        _bridge=new Node3D { Visible=false }; AddChild(_bridge);
        _bridge.AddChild(new MeshInstance3D { Position=new(0,0.045f,-0.5f),Mesh=new BoxMesh { Size=new(3,0.08f,2.8f) },MaterialOverride=Material(new(0.56f,0.38f,0.19f)) });
        for(var i=0;i<5;i++) _bridge.AddChild(new MeshInstance3D { Position=new(-1.2f+i*0.6f,0.091f,-0.5f),Mesh=new BoxMesh { Size=new(0.035f,0.012f,2.8f) },MaterialOverride=Material(new(0.25f,0.17f,0.1f)) });
    }
    private void WorldChanged(WorldNodeState state) { if(_bridge is not null) _bridge.Visible=(state.Consequences&1)!=0; }
    public override void _ExitTree()
    { if(_network is not null) { _network.ExplorationReceived-=Apply; _network.WorldNodeReceived-=WorldChanged; } }
}

public partial class StarterMap : Control
{
    private NetworkClient? _network;
    private StarterZoneState _zone;
    private ExplorationState? _state;
    private System.Numerics.Vector2 _position;
    public void Update(NetworkClient network,StarterZoneState zone,ExplorationState? state,System.Numerics.Vector2 position)
    { _network=network; _zone=zone; _state=state; _position=position; QueueRedraw(); }
    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero,Size),new(0.025f,0.035f,0.05f));
        if(_network?.Navigation is not { } grid || _state is not { } state) return;
        var scale=Mathf.Min(Size.X/grid.Width,Size.Y/grid.Height); var offset=(Size-new Vector2(grid.Width,grid.Height)*scale)*0.5f;
        bool Explored(System.Numerics.Vector2 position)
        { var cell=grid.Cell(position); return cell>=0 && cell<grid.CellCount && (state.Cells[cell/8]&(1<<(cell%8)))!=0; }
        Vector2 Point(System.Numerics.Vector2 position) => offset+new Vector2((position.X-grid.Origin.X)/grid.CellSize,(position.Y-grid.Origin.Y)/grid.CellSize)*scale;
        for(var z=0;z<grid.Height;z++) for(var x=0;x<grid.Width;x++)
        { var cell=z*grid.Width+x; if((state.Cells[cell/8]&(1<<(cell%8)))==0) continue;
            DrawRect(new(offset+new Vector2(x,z)*scale,new(scale+0.2f,scale+0.2f)),grid.IsBlocked(x,z) ? new(0.25f,0.25f,0.28f) : new Color(0.2f,0.32f,0.26f)); }
        if(Explored(_zone.TownPosition)) DrawCircle(Point(_zone.TownPosition),4,new(0.85f,0.72f,0.38f));
        foreach(var place in state.Places) DrawCircle(Point(place.Position),3,new(0.2f,0.9f,0.7f));
        if(_network.LatestWorldNode is { } node && Explored(node.Position)) DrawCircle(Point(node.Position),3,new(0.5f,0.65f,1));
        DrawCircle(Point(_position),4,new(1,1,1));
        DrawLine(Point(_position),Point(_position)+Vector2.Up*9,new(1,1,1),1.5f);
    }
}
