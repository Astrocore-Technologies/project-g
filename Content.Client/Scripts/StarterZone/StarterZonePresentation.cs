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
    private Label _guideText=null!;
    private UiWindow _guide=null!, _worldWindow=null!;
    private StarterMap _worldMap=null!;
    private SubViewport _worldMapTerrain=null!;
    private Camera3D _worldMapCamera=null!;
    private bool _centerMapOnOpen;
    private VBoxContainer _placeList=null!;
    private Label _mapDetail=null!, _pinText=null!;
    private UiMinimapView _minimap=null!;
    private Node3D _bridge=null!;
    private double _refresh;
    private readonly HashSet<ushort> _places=new();
    private static readonly string[] Steps=["ПКМ / удержание — движение", "ЛКМ — удар по направлению курсора; удержание — повтор", "QWER / ASDF — умения; Space — рывок", "T / Y / U — способность Эхо; Tab — блок; Shift — парирование", "Исследовать окрестности", "H у хранителя — помочь переправе", "P — проверить доступные профессии"];
    public void Initialize(NetworkClient network, PlayerController player, StarterZoneState zone, bool buildLegacyScenery = true)
    {
        _network=network; _player=player; _zone=zone;
        _network.ExplorationReceived+=Apply; _network.WorldNodeReceived+=WorldChanged;
        var canvas=new CanvasLayer { Layer=4 }; AddChild(canvas);
        var root=new Control { Theme=GameUi.CreateTheme(),MouseFilter=Control.MouseFilterEnum.Ignore }; canvas.AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _minimap=GD.Load<PackedScene>("res://UI/Components/Minimap.tscn").Instantiate<UiMinimapView>(); root.AddChild(_minimap);
        _minimap.GetNode<Button>("%OpenMap").Pressed+=ToggleMap;
        _worldWindow=new UiWindow { ToggleKey=Key.M }; canvas.AddChild(_worldWindow); _worldWindow.Build("Карта мира",new(1180,656)); _worldWindow.CloseRequested+=_worldWindow.Close;
        var mapView=GD.Load<PackedScene>("res://UI/Screens/WorldMapView.tscn").Instantiate<Control>(); _worldWindow.Body.AddChild(mapView);
        _worldMap=mapView.GetNode<StarterMap>("%Map"); _worldMap.Interactive=true;
        _worldMapTerrain=mapView.GetNode<SubViewport>("%Terrain"); _worldMapTerrain.World3D=GetViewport().World3D;
        _worldMapCamera=mapView.GetNode<Camera3D>("%Camera");
        _worldMap.TerrainTexture=_worldMapTerrain.GetTexture(); _worldMap.HasTerrainRender=true; _worldMap.TerrainMatchesView=true;
        _worldMap.ViewChanged+=RefreshMapTerrain;
        mapView.GetNode<Button>("%ZoomOut").Pressed+=()=>_worldMap.ZoomBy(.8f);
        mapView.GetNode<Button>("%ZoomIn").Pressed+=()=>_worldMap.ZoomBy(1.25f);
        mapView.GetNode<Button>("%Overview").Pressed+=()=>_worldMap.ResetView();
        mapView.GetNode<Button>("%Center").Pressed+=()=>_worldMap.FocusAt(_player.PredictedPosition);
        mapView.GetNode<CheckBox>("%ShowPlaces").Toggled+=value=> { _worldMap.ShowPlaces=value; _worldMap.QueueRedraw(); };
        mapView.GetNode<Label>("%Region").Text=zone.RegionName;
        _mapDetail=mapView.GetNode<Label>("%Detail"); _placeList=mapView.GetNode<VBoxContainer>("%Places"); _pinText=mapView.GetNode<Label>("%PinText");
        mapView.GetNode<Button>("%ClearPin").Pressed+=()=> { _worldMap.Pin=null; _worldMap.QueueRedraw(); _pinText.Text="Отметка убрана"; };
        _worldMap.PinChanged+=position=>_pinText.Text=$"Личная отметка\nX {position.X:0.0} · Z {position.Y:0.0}";
        _guide=new UiWindow { ToggleKey=Key.F1 }; canvas.AddChild(_guide); _guide.Build("Памятка путешественника",new(820,640)); _guide.CloseRequested+=_guide.Close;
        _guideText=UiComposition.Paragraph(UiComposition.Scroll(_guide.Body),"",16);
        _guide.Body.AddChild(GameUi.Button("Продолжить путь",_guide.Close));
        RefreshGuide(); BuildTown(buildLegacyScenery);
        if(network.LatestExploration is {} exploration) Apply(exploration);
        if(network.LatestWorldNode is { } state) WorldChanged(state);
    }
    private void RefreshGuide()
    {
        var text=_zone.GuideName+"\nДобро пожаловать в "+_zone.TownName+"!\n\n";
        var flags=_exploration?.Tutorial ?? 0;
        // Bit 3 is reserved for the deferred Echo tutorial; keep persisted bit positions unchanged.
        for(var i=0;i<Steps.Length;i++) if(i!=3) text+=((flags&(1<<i))!=0 ? "✓ " : "○ ")+Steps[i]+"\n";
        text+="\nCtrl + ЛКМ — одиночный удар в направлении курсора.\nTab — блок; Shift — парирование.\n";
        _guideText.Text=text+"\nI — вещи, K — характеристики и навыки, C — добыча, крафт и ремонт.\nB — обмен, J — кузница, монеты и местный рынок.\nN — группа, O — гильдия. F2 — разговор рядом с NPC, L — поручения.\nИсследуй мир и пробуй разные действия: новые возможности\nпоявляются по мере твоего пути. Подсказки не дают наград.\nV — PvP/смерть; F1 — открыть или закрыть эту памятку.";
    }
    public void ToggleMap() { if(_worldWindow.Visible) _worldWindow.Close(); else if(_worldWindow.Open()) _centerMapOnOpen=true; }
    private void RefreshMapTerrain()
    {
        if(!_worldWindow.Visible || _network.Navigation is not {} grid || _worldMap.Size.Y<=0) return;
        var view=_worldMap.WorldView; var longest=Math.Max(_worldMap.Size.X,_worldMap.Size.Y);
        if(view.Size.X<=.001f || view.Size.Y<=.001f || longest<=0) return;
        _worldMapTerrain.Size=new(Math.Max(64,(int)(640*_worldMap.Size.X/longest)),Math.Max(64,(int)(640*_worldMap.Size.Y/longest)));
        _worldMapCamera.Position=new(view.GetCenter().X,_player.Position.Y+120,view.GetCenter().Y);
        _worldMapCamera.Size=view.Size.Y; _worldMapCamera.CullMask=grid.Surface is not null?UiMapLayers.Scenery:1;
        _worldMapTerrain.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
    }
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
        _minimap.Update(_network,_zone,_exploration,position,_player.Position.Y,!GameUi.GameplayModalOpen);
        if(_worldWindow.Visible)
        {
            _worldMap.Update(_network,_zone,_exploration,position);
            if(_centerMapOnOpen) { _centerMapOnOpen=false; _worldMap.FocusAt(position); }
        }
        else _worldMapTerrain.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;
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
            var marker=new Node3D { Position=ProjectG.Regions.PublicSurfaceAnchors.Position(_network,place.Position) }; AddChild(marker);
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
        var guide=new Node3D { Position=ProjectG.Regions.PublicSurfaceAnchors.Position(_network,_zone.GuidePosition) }; AddChild(guide);
        guide.AddChild(ProjectG.Actors.ActorScenes.Load("GuideFigure").Instantiate<Node3D>());
        guide.AddChild(new Label3D { Position=new(0,2.1f,0),Text=_zone.GuideName+" • F1",Billboard=BaseMaterial3D.BillboardModeEnum.Enabled });
        AddChild(new Label3D { Position=ProjectG.Regions.PublicSurfaceAnchors.Position(_network,_zone.TownPosition,2.8f)+new Vector3(0,0,-4),Text=_zone.TownName,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled });
        if (!legacy) return;
        _bridge=new Node3D { Visible=false }; AddChild(_bridge);
        _bridge.AddChild(new MeshInstance3D { Position=new(0,0.045f,-0.5f),Mesh=new BoxMesh { Size=new(3,0.08f,2.8f) },MaterialOverride=Material(new(0.56f,0.38f,0.19f)) });
        for(var i=0;i<5;i++) _bridge.AddChild(new MeshInstance3D { Position=new(-1.2f+i*0.6f,0.091f,-0.5f),Mesh=new BoxMesh { Size=new(0.035f,0.012f,2.8f) },MaterialOverride=Material(new(0.25f,0.17f,0.1f)) });
    }
    private void WorldChanged(WorldNodeState state) { if(_bridge is not null) _bridge.Visible=(state.Consequences&1)!=0; }
    public override void _ExitTree()
    { if(_worldMap is not null) _worldMap.ViewChanged-=RefreshMapTerrain; if(_network is not null) { _network.ExplorationReceived-=Apply; _network.WorldNodeReceived-=WorldChanged; } }
}
