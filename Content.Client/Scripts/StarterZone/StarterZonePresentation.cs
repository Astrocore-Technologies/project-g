using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.Gameplay;
using ProjectG.UI;
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
    private UiWindow _guide=null!, _worldWindow=null!;
    private StarterMap _worldMap=null!;
    private VBoxContainer _placeList=null!;
    private Label _mapDetail=null!, _pinText=null!;
    private PanelContainer _mapPanel=null!;
    private Node3D _bridge=null!;
    private double _refresh;
    private readonly HashSet<ushort> _places=new();
    private static readonly string[] Steps=["ПКМ / удержание — движение", "ЛКМ по цели — подход и автоатака", "QWER / ASDF — умения; Space — рывок", "T / Y / U — способность Эхо; Tab — блок; Shift — парирование", "Исследовать окрестности", "H у хранителя — помочь переправе", "P — проверить доступные профессии"];
    public void Initialize(NetworkClient network, PlayerController player, StarterZoneState zone, bool buildLegacyScenery = true)
    {
        _network=network; _player=player; _zone=zone;
        _network.ExplorationReceived+=Apply; _network.WorldNodeReceived+=WorldChanged;
        var canvas=new CanvasLayer { Layer=4 }; AddChild(canvas);
        var root=new Control { Theme=GameUi.CreateTheme(),MouseFilter=Control.MouseFilterEnum.Ignore }; canvas.AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _mapPanel=new PanelContainer { Position=new(16,16),CustomMinimumSize=new(268,174),MouseFilter=Control.MouseFilterEnum.Stop }; root.AddChild(_mapPanel);
        var miniRow=new HBoxContainer(); _mapPanel.AddChild(miniRow);
        var frame=new UiRoundFrame(); miniRow.AddChild(frame);
        _map=new StarterMap { MouseFilter=Control.MouseFilterEnum.Ignore }; frame.Content.AddChild(_map); _map.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var info=new VBoxContainer { CustomMinimumSize=new(90,0),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; miniRow.AddChild(info);
        _location=UiComposition.Paragraph(info,zone.RegionName,14);
        info.AddChild(GameUi.Button("Карта · M",ToggleMap));
        GameUi.CompactHud(_mapPanel);
        _worldWindow=new UiWindow { ToggleKey=Key.M }; canvas.AddChild(_worldWindow); _worldWindow.Build("Карта мира",new(1180,656)); _worldWindow.CloseRequested+=_worldWindow.Close;
        var page=UiComposition.Page(_worldWindow,"map");
        var tools=new HBoxContainer(); page.AddChild(tools);
        tools.AddChild(GameUi.Button("−",()=>_worldMap.ZoomBy(.8f))); tools.AddChild(GameUi.Button("+",()=>_worldMap.ZoomBy(1.25f)));
        tools.AddChild(GameUi.Button("Обзор области",()=>_worldMap.ResetView()));
        var places=new CheckBox { Text="Известные места",ButtonPressed=true }; tools.AddChild(places); places.Toggled+=value=> { _worldMap.ShowPlaces=value; _worldMap.QueueRedraw(); };
        var row=new HBoxContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; page.AddChild(row);
        var mapPanel=new PanelContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; row.AddChild(mapPanel);
        _worldMap=new StarterMap { Interactive=true,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(280,260),ClipContents=true }; mapPanel.AddChild(_worldMap);
        var detail=UiComposition.Card(row,230);
        detail.AddChild(GameUi.Text(zone.RegionName,22));
        _mapDetail=UiComposition.Paragraph(detail,"Показаны только исследованные территории.",14);
        detail.AddChild(new HSeparator());
        _placeList=UiComposition.Scroll(detail);
        _pinText=UiComposition.Paragraph(detail,"ЛКМ — личная отметка на открытой территории.\nПКМ — перемещение карты; колесо — масштаб.",12);
        detail.AddChild(GameUi.Button("Убрать отметку",()=> { _worldMap.Pin=null; _worldMap.QueueRedraw(); _pinText.Text="Отметка убрана"; }));
        _worldMap.PinChanged+=position=>_pinText.Text=$"Личная отметка\nX {position.X:0.0} · Z {position.Y:0.0}\nСохраняется до выхода из региона";
        page.AddChild(GameUi.Text("С ↑   В →   Ю ↓   З ←     ·     Неизведанная территория скрыта",12));
        _guide=new UiWindow { ToggleKey=Key.F1 }; canvas.AddChild(_guide); _guide.Build("Памятка путешественника",new(820,640)); _guide.CloseRequested+=_guide.Close;
        _guideText=UiComposition.Paragraph(UiComposition.Scroll(_guide.Body),"",16);
        _guide.Body.AddChild(GameUi.Button("Продолжить путь",_guide.Close));
        RefreshGuide(); BuildTown(buildLegacyScenery);
        if(network.LatestWorldNode is { } state) WorldChanged(state);
    }
    private void RefreshGuide()
    {
        var text=_zone.GuideName+"\nДобро пожаловать в "+_zone.TownName+"!\n\n";
        var flags=_exploration?.Tutorial ?? 0;
        for(var i=0;i<Steps.Length;i++) text+=((flags&(1<<i))!=0 ? "✓ " : "○ ")+Steps[i]+"\n";
        _guideText.Text=text+"\nI — вещи, K — характеристики и навыки, C — добыча, крафт и ремонт.\nB — обмен, J — кузница, монеты и местный рынок.\nN — группа, O — гильдия. F2 — разговор рядом с NPC, L — поручения.\nИсследуй мир и пробуй разные действия: новые возможности\nпоявляются по мере твоего пути. Подсказки не дают наград.\nV — PvP/смерть; F1 — открыть или закрыть эту памятку.";
    }
    public void ToggleMap() { if(_worldWindow.Visible) _worldWindow.Close(); else _worldWindow.Open(); }
    public void ToggleGuide() { if(_guide.Visible) _guide.Close(); else _guide.Open(); }
    public override void _UnhandledInput(InputEvent input)
    {
        if(GameUi.GameplayModalOpen || input is not InputEventKey { Pressed:true,Echo:false } key) return;
        if(key.PhysicalKeycode==Key.F1) { ToggleGuide(); GetViewport().SetInputAsHandled(); }
        if(key.PhysicalKeycode==Key.M) { ToggleMap(); GetViewport().SetInputAsHandled(); }
    }
    public override void _Process(double delta)
    {
        _refresh+=delta; if(_refresh<0.25 || _network.Navigation is null) return; _refresh=0;
        var position=_player.PredictedPosition;
        var safe=_network.LatestPvpZone is {} z && position.X>=z.MinX && position.X<=z.MaxX && position.Y>=z.MinZ && position.Y<=z.MaxZ;
        _location.Text=$"{_zone.RegionName}\n\n{(safe?"Город · без PvP":"Окрестности")}\nX {position.X:0} · Z {position.Y:0}";
        _mapPanel.Visible=!GameUi.GameplayModalOpen;
        _map.Update(_network,_zone,_exploration,position);
        if(_worldWindow.Visible) _worldMap.Update(_network,_zone,_exploration,position);
    }
    private void Apply(ExplorationState state)
    {
        if(state.OwnerId!=_player.EntityId) return;
        _exploration=state; RefreshGuide();
        foreach(var child in _placeList.GetChildren()) { _placeList.RemoveChild(child); child.QueueFree(); }
        void Place(string name,System.Numerics.Vector2 position)
        {
            _placeList.AddChild(GameUi.Button(name,()=> { _worldMap.FocusAt(position); _mapDetail.Text=$"{name}\nX {position.X:0.0} · Z {position.Y:0.0}\nИзвестное место"; }));
        }
        if(_network.Navigation is {} grid) { var c=grid.Cell(_zone.TownPosition); if(c>=0 && c<grid.CellCount && (state.Cells[c/8]&(1<<(c%8)))!=0) Place(_zone.TownName,_zone.TownPosition); }
        foreach(var known in state.Places) Place(known.Name,known.Position);
        if(_placeList.GetChildCount()==0) UiComposition.Paragraph(_placeList,"Исследуйте окрестности, чтобы открыть места на карте.",14);
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
    private void BuildTown(bool legacy)
    {
        if (legacy)
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
        }
        var guide=new Node3D { Position=new(_zone.GuidePosition.X,0,_zone.GuidePosition.Y) }; AddChild(guide);
        guide.AddChild(new MeshInstance3D { Position=new(0,0.8f,0),Mesh=new CapsuleMesh { Radius=0.3f,Height=1.6f },MaterialOverride=Material(new(0.25f,0.5f,0.85f)) });
        guide.AddChild(new Label3D { Position=new(0,2.1f,0),Text=_zone.GuideName+" • F1",Billboard=BaseMaterial3D.BillboardModeEnum.Enabled });
        AddChild(new Label3D { Position=new(_zone.TownPosition.X,2.8f,_zone.TownPosition.Y-4),Text=_zone.TownName,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled });
        if (!legacy) return;
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
    private float _zoom=1;
    private Vector2 _pan;
    public bool Interactive { get; set; }
    public bool ShowPlaces { get; set; }=true;
    public System.Numerics.Vector2? Pin { get; set; }
    public event Action<System.Numerics.Vector2>? PinChanged;
    public void Update(NetworkClient network,StarterZoneState zone,ExplorationState? state,System.Numerics.Vector2 position)
    { _network=network; _zone=zone; _state=state; _position=position; QueueRedraw(); }
    private float CellScale => _network?.Navigation is {} g ? Mathf.Min(Size.X/g.Width,Size.Y/g.Height)*_zoom:1;
    private Vector2 Offset => _network?.Navigation is {} g ? (Size-new Vector2(g.Width,g.Height )*CellScale)*.5f+_pan:Vector2.Zero;
    private Vector2 Point(System.Numerics.Vector2 p) => _network?.Navigation is {} g ? Offset+new Vector2((p.X-g.Origin.X)/g.CellSize,(p.Y-g.Origin.Y)/g.CellSize )*CellScale:Vector2.Zero;
    private bool Explored(System.Numerics.Vector2 p)
    { if(_network?.Navigation is not {} g || _state is not {} s) return false; var c=g.Cell(p); return c>=0 && c<g.CellCount && c/8<s.Cells.Length && (s.Cells[c/8]&(1<<(c%8)))!=0; }
    public void ZoomBy(float value) { _zoom=Mathf.Clamp(_zoom*value,1,5); if(_zoom==1) _pan=Vector2.Zero; QueueRedraw(); }
    public void ResetView() { _zoom=1; _pan=Vector2.Zero; QueueRedraw(); }
    public void FocusAt(System.Numerics.Vector2 p) { _zoom=Mathf.Max(2,_zoom); _pan=Vector2.Zero; _pan=Size/2-Point(p); QueueRedraw(); }
    public override void _GuiInput(InputEvent ev)
    {
        if(!Interactive || _network?.Navigation is not {} grid) return;
        if(ev is InputEventMouseMotion motion && motion.ButtonMask.HasFlag(MouseButtonMask.Right)) { _pan+=motion.Relative; _pan=_pan.Clamp(-Size*_zoom,Size*_zoom); QueueRedraw(); AcceptEvent(); }
        if(ev is not InputEventMouseButton { Pressed:true } click) return;
        if(click.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) { ZoomBy(click.ButtonIndex==MouseButton.WheelUp?1.2f:1/1.2f); AcceptEvent(); }
        if(click.ButtonIndex!=MouseButton.Left) return;
        var c=(click.Position-Offset)/CellScale;
        var p=new System.Numerics.Vector2(grid.Origin.X+c.X*grid.CellSize,grid.Origin.Y+c.Y*grid.CellSize);
        if(Explored(p)) { Pin=p; PinChanged?.Invoke(p); QueueRedraw(); } AcceptEvent();
    }
    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero,Size),new Color("e8d9b9"));
        if(_network?.Navigation is not {} grid || _state is not {} state) return;
        var scale=CellScale; var offset=Offset;
        for(var z=0;z<grid.Height;z++) for(var x=0;x<grid.Width;x++)
        {
            var cell=z*grid.Width+x; if(cell/8>=state.Cells.Length || (state.Cells[cell/8]&(1<<(cell%8)))==0) continue;
            DrawRect(new(offset+new Vector2(x,z)*scale,new(scale+.2f,scale+.2f)),grid.IsBlocked(x,z)?new Color("799384"):new Color("b9c5a2"));
        }
        if(ShowPlaces)
        {
            if(Explored(_zone.TownPosition)) Marker(_zone.TownPosition,_zone.TownName,new("b68a43"));
            foreach(var place in state.Places) Marker(place.Position,place.Name,new("387957"));
            if(_network.CurrentRegion is {} region && Explored(region.Exit)) Marker(region.Exit,"Переход",new("5095af"));
            if(_network.LatestWorldNode is {} node && Explored(node.Position)) Marker(node.Position,"Хранитель",new("806994"));
        }
        if(Pin is {} pin && Explored(pin)) { var q=Point(pin); DrawCircle(q,8,new Color("a1443d"),false,2); DrawLine(q-Vector2.Up*12,q+Vector2.Up*12,new Color("a1443d"),2); }
        var pos=Point(_position); DrawCircle(pos,6,new Color("274b64")); DrawCircle(pos,3,Colors.White); DrawLine(pos,pos+Vector2.Up*11,Colors.White,2);
        DrawString(ThemeDB.FallbackFont,new(10,22),"С",HorizontalAlignment.Left,-1,16,new Color("273d49"));
    }
    private void Marker(System.Numerics.Vector2 p,string text,Color color)
    {
        var point=Point(p); DrawCircle(point,5,color); DrawCircle(point,6,new Color("fff8e8"),false,1.5f);
        if(Interactive) DrawString(ThemeDB.FallbackFont,point+new Vector2(9,5),text,HorizontalAlignment.Left,-1,14,new Color("273d49"));
    }
}
