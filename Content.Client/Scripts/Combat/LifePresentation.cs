using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Combat;

public partial class LifePresentation:CanvasLayer
{
    private NetworkClient _network=null!;
    private NetworkEntityId _owner;
    private UiWindow _window=null!;
    private Label _timer=null!,_status=null!;
    private Button _respawn=null!;
    private Button _open=null!;
    private ActorLifeState _life;
    private double _clock,_received,_sent,_refresh;
    private uint _sequence;
    private RespawnCommand? _pending;
    public void Initialize(NetworkClient network,NetworkEntityId owner)
    {
        _network=network;_owner=owner;Layer=35;
        _window=new UiWindow();AddChild(_window);_window.Build("Вы погибли",new(620,420));
        _window.CloseRequested+=_window.Close;
        _open=GD.Load<PackedScene>("res://UI/Components/DeathReopen.tscn").Instantiate<Button>();AddChild(_open);_open.Pressed+=()=>_window.Open();
        var view=GD.Load<PackedScene>("res://UI/Screens/DeathView.tscn").Instantiate<Control>();_window.Body.AddChild(view);
        _timer=view.GetNode<Label>("%Timer");_status=view.GetNode<Label>("%Status");_respawn=view.GetNode<Button>("%RespawnButton");_respawn.Pressed+=Request;
        view.GetNode<Button>("%DeathLog").Pressed+=()=>{_window.Close();GameUi.Navigate("death-log");};
        network.LifeReceived+=Apply;network.RespawnReceived+=Result;
        if(network.KnownLife.TryGetValue(owner,out var life))Apply(life);
    }
    public void Apply(ActorLifeState life)
    {
        if(life.EntityId!=_owner)return;
        var wasDead=_life.Dead;_life=life;_received=_clock;
        _refresh=0;_respawn.Disabled=!life.Dead || life.RespawnRemaining>0 || _pending is not null;
        if(life.Dead&&!wasDead){_status.Text="";_window.Open();}
        if(!life.Dead){_pending=null;_window.Close();}
    }
    private void Request()
    {
        if(!_life.Dead || _pending is not null)return;
        _pending=new RespawnCommand(++_sequence,_life.DeathId);_sent=_clock;_status.Text="Ожидаем сохранения…";_network.SendRespawn(_pending.Value);
    }
    private void Result(RespawnResult result)
    {
        if(_pending?.Sequence!=result.Sequence)return;_pending=null;
        _status.Text=result.Outcome switch
        {RespawnOutcome.Accepted or RespawnOutcome.AlreadyProcessed=>"",RespawnOutcome.NotReady=>"Возрождение пока недоступно.",_=>"Состояние изменилось. Повторите действие."};
    }
    public override void _Process(double delta)
    {
        _clock+=delta;
        _open.Visible=_life.Dead&&!_window.Visible&&!GameUi.GameplayModalOpen;
        if(_pending is {} command && _clock-_sent>=2){_sent=_clock;_network.SendRespawn(command);}
        if(!_life.Dead || _clock<_refresh)return;_refresh=_clock+.2;
        var elapsed=_clock-_received;var corpse=Math.Max(0,_life.CorpseRemaining-elapsed);var ready=Math.Max(0,_life.RespawnRemaining-elapsed);
        _timer.Text=(corpse>0?$"Воскрешение на месте доступно ещё {Math.Ceiling(corpse)} с.":"Тело исчезло. Доступно возрождение.")+
            (ready>0?$"\nВозрождение через {Math.Ceiling(ready)} с.":"");
        _respawn.Disabled=!_life.Dead || ready>0 || _pending is not null;
    }
    public override void _ExitTree()
    {if(_network is not null){_network.LifeReceived-=Apply;_network.RespawnReceived-=Result;}}
}
