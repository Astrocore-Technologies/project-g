using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.WorldStory;
/// <summary>Public keeper/lore presentation. Intentions do not predict world consequences.</summary>
public partial class WorldNodePresentation : Node3D
{
    private NetworkClient _network=null!;
    private PlayerController _player=null!;
    private WorldNodeState? _state;
    private CanvasLayer _ui=null!;
    private UiWindow _panel=null!;
    private Label _rumor=null!;
    private Label _line=null!;
    private Label _result=null!;
    private Label3D _keeper=null!;
    private Button _repair=null!;
    private Button _patrol=null!;
    private uint _sequence;
    private uint _pending;
    private double _pendingUntil;
    private bool _alive=true;
    public void Initialize(NetworkClient network,PlayerController player)
    {
        _network=network; _player=player;
        _ui=new CanvasLayer { Layer=23 }; AddChild(_ui);
        _rumor=new Label { Position=new(24,410),Size=new(300,52),AutowrapMode=TextServer.AutowrapMode.WordSmart,Text="H — поговорить с хранителем переправы",MouseFilter=Control.MouseFilterEnum.Ignore }; _ui.AddChild(_rumor); _rumor.Hide();
        _panel=new UiWindow { ToggleKey=Key.H }; _ui.AddChild(_panel); _panel.Build("Хранитель переправы",new(900,580)); _panel.CloseRequested+=_panel.Close;
        var list=UiComposition.Scroll(_panel.Body);
        list.AddChild(new Label { Text="Переправа • общий сюжет мира" });
        _line=new Label { CustomMinimumSize=new(425,0),AutowrapMode=TextServer.AutowrapMode.WordSmart }; list.AddChild(_line);
        _repair=new Button { Text="Помочь ремонту" }; _repair.Pressed+=()=>Send(WorldNodeAction.Repair); list.AddChild(_repair);
        _patrol=new Button { Text="Помочь дозору (после боя с восточным монстром)" }; _patrol.Pressed+=()=>Send(WorldNodeAction.Patrol); list.AddChild(_patrol);
        _result=new Label { CustomMinimumSize=new(425,0),AutowrapMode=TextServer.AutowrapMode.WordSmart }; list.AddChild(_result);
        var close=new Button { Text="Закрыть" }; close.Pressed+=()=>_panel.Close(); list.AddChild(close);
        var actor=new Node3D(); AddChild(actor);
        actor.AddChild(new MeshInstance3D { Position=new(0,0.8f,0),Mesh=new CapsuleMesh { Radius=0.35f,Height=1.6f },MaterialOverride=new StandardMaterial3D { AlbedoColor=new(0.1f,0.65f,0.75f) } });
        actor.AddChild(new MeshInstance3D { Position=new(0,0.05f,0),Mesh=new CylinderMesh { TopRadius=1.1f,BottomRadius=1.1f,Height=0.06f },MaterialOverride=new StandardMaterial3D { AlbedoColor=new(0.15f,0.45f,0.7f) } });
        _keeper=new Label3D { Position=new(0,2.7f,0),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,FontSize=24,Width=480,AutowrapMode=TextServer.AutowrapMode.WordSmart }; actor.AddChild(_keeper);
        if(network.LatestWorldNode is { } state) Apply(state);
    }
    public void Apply(WorldNodeState state)
    {
        if(_state is { } old && state.Revision<=old.Revision) return;
        _state=state; Position=new(state.Position.X,0,state.Position.Y);
        _keeper.Text=state.KeeperName+"\n"+state.KeeperLine;
        _line.Text=state.KeeperLine; _rumor.Text="Слух: "+state.Rumor+"  •  H — хранитель";
    }
    public void ApplyAlive(bool alive) { _alive=alive; if(!alive) _panel.Close(); }
    public override void _Process(double delta)
    {
        if(_pending!=0 && Time.GetTicksMsec()/1000d>=_pendingUntil) { _pending=0; _result.Text="Ответ задерживается. Мир обновится после подтверждения сервера."; }
        var near=_state is { } state && System.Numerics.Vector2.DistanceSquared(_player.PredictedPosition,state.Position)<=4;
        _repair.Disabled=!_alive || !near || _pending!=0 || _state is null || (_state.Value.Consequences&1)!=0;
        _patrol.Disabled=!_alive || !near || _pending!=0 || _state is null || (_state.Value.Consequences&2)!=0;
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if(input is InputEventKey { Pressed:true,Echo:false,Keycode:Key.H })
        { if(GameUi.GameplayModalOpen)return; Toggle(); if(_panel.Visible) _result.Text="Для участия остановитесь рядом с хранителем. Ваш вклад учитывается один раз."; GetViewport().SetInputAsHandled(); }
    }
    public void Toggle() { if(_panel.Visible) _panel.Close(); else _panel.Open(); }
    private void Send(WorldNodeAction action)
    {
        if(!_alive || _pending!=0) return;
        if(++_sequence==0) ++_sequence;
        _pending=_sequence; _pendingUntil=Time.GetTicksMsec()/1000d+10;
        _result.Text="Хранитель ждёт подтверждения вашего вклада…"; _network.SendWorldNode(new(_sequence,action));
    }
    public void Result(WorldNodeResult result)
    {
        if(result.Sequence!=_sequence) return; _pending=0;
        _result.Text=result.Outcome switch
        {
            WorldNodeOutcome.Accepted => "Вклад сохранён. Общий исход зависит от помощи путников.",
            WorldNodeOutcome.AlreadyContributed => "Ваш вклад уже учтён.",
            WorldNodeOutcome.Unavailable => "Помощь пока не подходит: сначала исследуйте восточную угрозу, либо это дело уже завершено.",
            WorldNodeOutcome.TooFar => "Подойдите к хранителю переправы.",
            WorldNodeOutcome.Busy => "Сначала остановитесь и завершите действие.",
            WorldNodeOutcome.InvalidState => "Сейчас персонаж не может участвовать.",
            _ => "Слишком много запросов. Попробуйте ещё раз."
        };
    }
}
