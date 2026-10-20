using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;

namespace ProjectG.Combat;

public partial class TavernPresentation:CanvasLayer
{
    private NetworkClient _network=null!;
    private PlayerController _player=null!;
    private UiWindow _window=null!;
    private TavernView _view=null!;
    private ProjectG.Progression.RespecPresentation _respec=null!;
    private QuestNpcSpawn _npc;
    private TavernCommand? _pending;
    private string _status="";
    private double _clock,_sent,_refresh;
    public void Initialize(NetworkClient network,PlayerController player)
    {
        _network=network;_player=player;Layer=22;
        _window=new UiWindow();AddChild(_window);_window.Build("Таверна",new(820,550));_window.CloseRequested+=Close;
        _view=GD.Load<PackedScene>("res://UI/Screens/TavernView.tscn").Instantiate<TavernView>();_window.Body.AddChild(_view);
        _view.BindRequested+=Request;_view.CloseRequested+=Close;
        _respec=new();AddChild(_respec);_respec.Initialize(network,player);_view.RespecRequested+=OpenRespec;
        network.TavernReceived+=Apply;network.TavernResultReceived+=Result;
    }
    public void Open(QuestNpcSpawn npc)
    {
        if(npc.Role!="innkeeper"||!_player.IsAlive||!_window.Open())return;
        _npc=npc;_status="";_player.StopMovement();_player.GetNodeOrNull<CombatPresentation>("CombatPresentation")?.CancelBasicAttack();Render();
    }
    private void Close()=>_window.Close();
    private void OpenRespec(){Close();_respec.Open(_npc);}
    private void Request()
    {
        if(_pending is not null||!_npc.EntityId.IsValid)return;
        _pending=_network.NewTavernCommand(_npc.EntityId);_sent=_clock;_network.SendTavern(_pending.Value);Render();
    }
    private void Apply(TavernState state){if(state.OwnerId==_player.EntityId)Render();}
    private void Result(TavernResult result)
    {
        if(_pending?.Sequence!=result.Sequence)return;_pending=null;
        _status=result.Outcome switch
        {
            TavernOutcome.Accepted=>"Это место возвращения сохранено.",TavernOutcome.AlreadyBound=>"Эта таверна уже назначена.",
            TavernOutcome.TooFar=>"Подойдите ближе к трактирщику.",TavernOutcome.InCombat=>"Сначала выйдите из боя.",
            TavernOutcome.InvalidState=>"Услуга доступна живому персонажу.",TavernOutcome.RateLimited=>"Слишком много запросов. Попробуйте ещё раз.",
            _=>"Таверна недоступна. Закройте окно и поговорите снова."
        };Render();
    }
    private void Render()=>_view.Render(_npc.Name??"Таверна",_network.LatestTavern is {} home&&home.OwnerId==_player.EntityId?home:null,_player.IsAlive,
        _network.LatestDefense?.InCombatRemaining>0,_pending is not null,_status);
    public override void _Process(double delta)
    {
        _clock+=delta;
        if(_pending is {} command&&_clock-_sent>=2){_sent=_clock;_network.SendTavern(command);}
        if(!_window.Visible)return;
        if(!_player.IsAlive){Close();return;}
        if(_clock>=_refresh){_refresh=_clock+.2;Render();}
    }
    public override void _ExitTree()
    {
        if(_network is null)return;
        _network.TavernReceived-=Apply;_network.TavernResultReceived-=Result;
        _view.BindRequested-=Request;_view.CloseRequested-=Close;_window.CloseRequested-=Close;
        _view.RespecRequested-=OpenRespec;
    }
}
