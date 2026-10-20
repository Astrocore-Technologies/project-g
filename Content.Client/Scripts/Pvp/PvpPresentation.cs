using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Pvp;
public partial class PvpPresentation:CanvasLayer
{
    private NetworkClient _network=null!;private PlayerController _player=null!;
    private UiWindow _panel=null!;private Label _hud=null!,_details=null!,_status=null!;
    private CheckBox _ack=null!;private Button _peace=null!,_voluntary=null!,_criminal=null!,_respawn=null!;
    private PvpState _state;private PvpCommand? _pending;private double _clock,_received,_sent,_channelAt,_refresh;
    private PickupChannelState _channel;
    public void Initialize(NetworkClient network,PlayerController player)
    {
        _network=network;_player=player;Layer=8;
        _hud=GD.Load<PackedScene>("res://UI/Components/PvpStatus.tscn").Instantiate<Label>();AddChild(_hud);
        _panel=new UiWindow { ToggleKey=Key.V }; AddChild(_panel); _panel.Build("PvP, репутация и смерть",new(960,600)); _panel.CloseRequested+=_panel.Close;
        var view=GD.Load<PackedScene>("res://UI/Screens/PvpView.tscn").Instantiate<Control>(); _panel.Body.AddChild(view);
        _details=view.GetNode<Label>("%Details"); _status=view.GetNode<Label>("%Status"); _ack=view.GetNode<CheckBox>("%Acknowledge");
        _peace=view.GetNode<Button>("%Peace"); _voluntary=view.GetNode<Button>("%Voluntary"); _criminal=view.GetNode<Button>("%Criminal"); _respawn=view.GetNode<Button>("%Respawn");
        _peace.Pressed+=()=>Mode(PvpMode.Peaceful); _voluntary.Pressed+=()=>Mode(PvpMode.Voluntary); _criminal.Pressed+=()=>Mode(PvpMode.Criminal);
        _respawn.Pressed+=()=>_network.SendRespawn(_network.NewRespawnCommand(_state.DeathId));
        network.PvpStateReceived+=State;network.PvpResultReceived+=Result;network.PickupChannelReceived+=Channel;
    }
    public override void _ExitTree(){if(_network is null)return;_network.PvpStateReceived-=State;_network.PvpResultReceived-=Result;_network.PickupChannelReceived-=Channel;}
    public override void _UnhandledInput(InputEvent e){if(PcInput.Pressed("pvp",e)){if(GameUi.GameplayModalOpen)return; Toggle();GetViewport().SetInputAsHandled();}}
    public void Toggle() { if(_panel.Visible) _panel.Close(); else _panel.Open(); }
    private void Mode(PvpMode mode){if(mode==PvpMode.Criminal&&!_ack.ButtonPressed)return;Request(new(_state.LastSequence+1,PvpAction.Mode,mode,mode==PvpMode.Criminal,0));}
    private void Request(PvpCommand c){if(_pending is not null||_state.OwnerId!=_player.EntityId)return;_pending=c;_sent=_clock;_status.Text="Ожидаем сохранения…";_network.SendPvp(c);}
    private void State(PvpState s){if(s.OwnerId!=_player.EntityId)return;_state=s;_received=_clock;}
    private void Channel(PickupChannelState c){if(c.OwnerId!=_player.EntityId)return;_channel=c;_channelAt=_clock;}
    private void Result(PvpResult r){if(_pending?.Sequence!=r.Sequence)return;_pending=null;_status.Text=r.Outcome switch{PvpOutcome.Accepted=>"Сохранено.",PvpOutcome.AlreadyProcessed=>"Запрос уже обработан.",PvpOutcome.NotReady=>"Дождитесь окончания таймера или активного эффекта.",PvpOutcome.Protected=>"Выйдите из города, чтобы включить PvP.",PvpOutcome.InvalidState=>"Действие недоступно в текущем состоянии.",PvpOutcome.RateLimited=>"Слишком много действий; попробуйте снова.",_=>"Запрос устарел. Получите текущее состояние."};}
    public override void _Process(double delta)
    {
        _clock+=delta;if(_pending is {} c&&_clock-_sent>=2){_sent=_clock;_network.SendPvp(c);}
        if(_clock<_refresh)return;_refresh=_clock+.2;var elapsed=_clock-_received;
        var combat=Math.Max(0,_state.CombatSeconds-elapsed);var respawn=Math.Max(0,_state.RespawnSeconds-elapsed);
        var channel=Math.Max(0,_channel.RemainingSeconds-(_clock-_channelAt));var mode=_state.Mode switch{PvpMode.Voluntary=>"PvP",PvpMode.Criminal=>"Нападение на мирных",_=>"Мирный"};
        _hud.Text=(_state.Dead?"Вы погибли · V":_state.Mode!=PvpMode.Peaceful?mode+" · V":"")+(combat>0?(_state.Mode==PvpMode.Peaceful&&!_state.Dead?"Боевой тег · ":" · тег ")+Math.Ceiling(combat)+" с":"")+(channel>0?(_state.Dead||_state.Mode!=PvpMode.Peaceful||combat>0?"\n":"")+"Подбор · "+channel.ToString("0.0")+" с":"");
        _details.Text="Репутация: "+_state.Reputation+" | PK: "+_state.Pk+"\n"+(_state.Dead?"Смерть "+(_state.PvpDeath?"PvP":"PvE")+". Потеря EXP: "+_state.ExperienceLost+". Возрождение через "+Math.Ceiling(respawn)+" с.\n"+(_state.Dropped?"Экипированный предмет выпал в мир.":_state.DropSkipped?"Лимит лута: предмет остался у вас.":"Вещи не выпали."):mode+". Боевой тег: "+Math.Ceiling(combat)+" с.");
        var busy=_pending is not null||_state.OwnerId!=_player.EntityId;_peace.Disabled=busy||_state.Dead||combat>0;_voluntary.Disabled=busy||_state.Dead;_criminal.Disabled=busy||_state.Dead||!_ack.ButtonPressed;_respawn.Disabled=busy||!_state.Dead||respawn>0;
        _hud.Visible=!GameUi.GameplayModalOpen && (_state.Dead || _state.Mode!=PvpMode.Peaceful || combat>0 || channel>0);
    }
}
