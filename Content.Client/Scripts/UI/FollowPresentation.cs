using Content.Shared.Movement;
using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;

namespace ProjectG.UI;

public partial class FollowPresentation : CanvasLayer
{
    private PlayerController _player=null!;
    private NetworkClient _network=null!;
    private IReadOnlyDictionary<NetworkEntityId,PlayerController> _players=null!;
    private PanelContainer _menu=null!;
    private Button _stop=null!;
    private Label _status=null!;
    private NetworkEntityId _selected;
    private FollowState? _state;
    private double _noticeUntil;
    public void Initialize(PlayerController player,NetworkClient network,IReadOnlyDictionary<NetworkEntityId,PlayerController> players)
    {
        _player=player;_network=network;_players=players;
        var view=GD.Load<PackedScene>("res://UI/Screens/PlayerContextView.tscn").Instantiate<Control>();AddChild(view);
        _menu=view.GetNode<PanelContainer>("%Menu");_status=view.GetNode<Label>("%FollowStatus");_stop=view.GetNode<Button>("%Stop");
        view.GetNode<Button>("%Follow").Pressed+=()=>{var id=_selected;Close();if(_players.ContainsKey(id))_player.RequestFollow(id);};
        _stop.Pressed+=()=>{Close();_player.StopMovement();};
        network.FollowReceived+=Apply; GameUi.ModalOpened+=Close;
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if(PcInput.Blocked(this)||!_player.IsAlive||!_network.RegionActive)return;
        if(!PcInput.Pressed(PcInput.Context,input)&&!PcInput.Pressed(PcInput.Follow,input))return;
        var point=(input as InputEventMouseButton)?.Position??GetViewport().GetMousePosition();var target=Pick(point);
        if(!target.IsValid)return;
        GetViewport().SetInputAsHandled();
        if(PcInput.Pressed(PcInput.Follow,input)){_player.RequestFollow(target);return;}
        _selected=target;_menu.Show();PcInput.ContextOpen=true;
        _menu.Size=_menu.Size.Max(_menu.GetCombinedMinimumSize());
        _menu.Position=point.Clamp(Vector2.Zero,GetViewport().GetVisibleRect().Size-_menu.Size);
        _stop.Disabled=_state?.Phase is not (FollowPhase.Active or FollowPhase.Recovering);
    }
    public override void _Input(InputEvent input)
    {
        if(_menu is null||!_menu.Visible)return;
        if(input.IsActionPressed("ui_cancel")||input is InputEventMouseButton{Pressed:true} mouse&&!_menu.GetGlobalRect().HasPoint(mouse.Position))
        {Close();GetViewport().SetInputAsHandled();}
    }
    private NetworkEntityId Pick(Vector2 mouse)
    {
        var camera=GetViewport().GetCamera3D();if(camera is null)return default;
        var closest=float.PositiveInfinity;var result=NetworkEntityId.Invalid;
        foreach(var pair in _players)
        {
            var target=pair.Value;if(pair.Key==_player.EntityId||!target.IsAlive||camera.IsPositionBehind(target.GlobalPosition))continue;
            var a=camera.UnprojectPosition(target.GlobalPosition-Vector3.Up);var b=camera.UnprojectPosition(target.GlobalPosition+Vector3.Up);
            var segment=b-a;var t=Math.Clamp((mouse-a).Dot(segment)/Math.Max(segment.LengthSquared(),.001f),0,1);
            if(mouse.DistanceTo(a+segment*t)>18)continue;
            var depth=camera.GlobalPosition.DistanceSquaredTo(target.GlobalPosition);
            if(depth<closest){closest=depth;result=pair.Key;}
        }
        return result;
    }
    private void Apply(FollowState state)
    {
        if(state.Phase!=FollowPhase.Rejected)
        {
            if(state.Phase is FollowPhase.Active or FollowPhase.Recovering)
            {
                if(MovementSimulation.IsSequenceNewer(_player.NavigationOrder,state.InputOrder))return;
                if(!_player.ConfirmFollow(state.InputOrder))return;
            }
            _state=state;
        }
        _status.Text=state.Phase switch
        {
            FollowPhase.Active=>"Следование включено",
            FollowPhase.Recovering=>"Восстанавливаем следование · до 5 секунд",
            _=>state.Reason switch {FollowReason.InvalidTarget=>"Эта цель недоступна",FollowReason.TooFar=>"Для Follow нужно подойти ближе 40 м",
                FollowReason.NotVisible=>"Цель вне прямой видимости",FollowReason.Restricted=>"Сейчас нельзя начать следование",
                FollowReason.LostTarget or FollowReason.NoPath=>"Следование остановлено: цель или путь недоступны",
                FollowReason.TargetLeft=>"Следование остановлено: игрок вышел или сменил регион",FollowReason.Died=>"Следование прекращено: персонаж погиб",
                _=>"Следование остановлено"}
        };
        _noticeUntil=Time.GetTicksMsec()/1000d+6;
    }
    public override void _Process(double delta)
    {
        if(_status is null)return;
        _status.Visible=!GameUi.GameplayModalOpen&&(_state?.Phase is FollowPhase.Active or FollowPhase.Recovering||Time.GetTicksMsec()/1000d<_noticeUntil);
        if(_menu.Visible&&(!_players.ContainsKey(_selected)||!_player.IsAlive||!GetWindow().HasFocus()))Close();
    }
    private void Close(){if(_menu is not null)_menu.Hide();PcInput.ContextOpen=false;}
    public override void _ExitTree(){Close();if(_network is not null)_network.FollowReceived-=Apply;GameUi.ModalOpened-=Close;}
}
