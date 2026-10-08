using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.Gameplay;
namespace ProjectG.Trading;
public partial class TradePresentation:CanvasLayer
{
    private NetworkClient _network=null!;
    private PlayerController _player=null!;
    private PanelContainer _panel=null!;
    private VBoxContainer _body=null!;
    private Label _status=null!;
    private InventoryState _inventory;
    private CraftState _materials;
    private TradeState? _trade;
    private uint _sequence;
    private bool _waiting;
    private bool _invitedByMe;
    private readonly HashSet<ulong> _selected=new();
    private readonly Dictionary<ushort,ushort> _quantities=new();
    public void Initialize(NetworkClient network,PlayerController player)
    {
        _network=network; _player=player; Layer=6;
        _panel=new(){Position=new Vector2(25,100),CustomMinimumSize=new Vector2(540,380),Visible=false}; AddChild(_panel);
        var scroll=new ScrollContainer(){CustomMinimumSize=new Vector2(540,380)}; _panel.AddChild(scroll);
        _body=new(){SizeFlagsHorizontal=Control.SizeFlags.ExpandFill}; scroll.AddChild(_body);
        _network.InventoryReceived+=Inventory; _network.CraftStateReceived+=Materials;
        _network.TradeStateReceived+=State; _network.TradeResultReceived+=Result;
        _network.PlayerSpawned+=Spawn; _network.PlayerDespawned+=Despawn;
        Render();
    }
    public override void _ExitTree()
    {
        if(_network is null)return;
        _network.InventoryReceived-=Inventory; _network.CraftStateReceived-=Materials;
        _network.TradeStateReceived-=State; _network.TradeResultReceived-=Result;
        _network.PlayerSpawned-=Spawn; _network.PlayerDespawned-=Despawn;
    }
    public override void _UnhandledKeyInput(InputEvent e)
    { if(e is InputEventKey {Pressed:true,Echo:false,PhysicalKeycode:Key.B}) { _panel.Visible=!_panel.Visible; GetViewport().SetInputAsHandled(); Render(); } }
    private void Inventory(InventoryState s){if(s.EntityId!=_player.EntityId)return; _inventory=s; _selected.RemoveWhere(h=>!s.Items.Any(i=>i.Handle==h&&!i.Equipped));Render();}
    private void Materials(CraftState s){if(s.OwnerId!=_player.EntityId)return;_materials=s;Render();}
    private void Spawn(PlayerSpawn s){Render();}
    private void Despawn(PlayerDespawn s){Render();}
    private void State(TradeState s)
    {
        if(s.OwnerId!=_player.EntityId)return;
        _trade=s; _waiting=false; _panel.Visible=true;
        if(s.Phase is TradePhase.Completed or TradePhase.Cancelled){_selected.Clear();_quantities.Clear();_invitedByMe=false;}
        Render();
    }
    private void Result(TradeResult r){if(r.Sequence!=_sequence)return;_waiting=false;Render();if(r.Outcome!=CraftOutcome.Accepted && (_trade is null || _trade.Value.Phase is TradePhase.Completed or TradePhase.Cancelled))_invitedByMe=false;_status.Text="Ответ сервера: "+ProjectG.Economy.EconomyPresentation.Outcome(r.Outcome);}
    private void Send(TradeAction action,NetworkEntityId partner,ulong session,uint revision,IReadOnlyList<ulong>? items=null,IReadOnlyList<MaterialAmount>? materials=null)
    {
        if(_waiting)return;
        _waiting=true;
        if(action==TradeAction.Invite)_invitedByMe=true;
        _network.SendTrade(new(++_sequence,action,session,partner,revision,items??Array.Empty<ulong>(),materials??Array.Empty<MaterialAmount>()));
        Render();
    }
    private void Text(string text){_body.AddChild(new Label(){Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart});}
    private void Button(string text,Action action,bool disabled=false){var b=new Button(){Text=text,Disabled=disabled||_waiting};b.Pressed+=action;_body.AddChild(b);}
    private void Render()
    {
        if(_body is null)return;
        foreach(var c in _body.GetChildren()){_body.RemoveChild(c);c.QueueFree();}
        Text("Обмен — B. Партнёр должен находиться рядом и стоять на месте.");
        _status=new Label(){Text=_waiting?"Ожидание сервера…":"Предложение меняется только после ответа сервера."};_body.AddChild(_status);
        if(_trade is not { } t || t.Phase is TradePhase.Completed or TradePhase.Cancelled)
        {
            if(_trade is {} closed)Text(closed.Phase==TradePhase.Completed?"Обмен завершён.":"Обмен отменён.");
            foreach(var p in _network.KnownPlayers.Values.Where(p=>p.EntityId!=_player.EntityId).OrderBy(p=>p.PlayerId.Value))
            { var id=p.EntityId;Button("Пригласить игрока "+p.PlayerId.Value,()=>Send(TradeAction.Invite,id,0,0)); }
            return;
        }
        Text(t.Phase==TradePhase.Invited?"Приглашение к обмену":"Каждый игрок отдельно подтверждает оба показанных предложения.");
        Text("Изменение предложения снимает подтверждения. Экипировка, крафт и ремонт во время обмена заблокированы.");
        if(t.Phase==TradePhase.Invited)
        {
            if(_invitedByMe)Text("Приглашение отправлено. Ожидаем согласия партнёра.");
            else Button("Принять приглашение",()=>Send(TradeAction.Accept,t.PartnerId,t.SessionId,t.Revision));
        }
        else
        {
            Text("Ваше предложение: "+(t.OwnAccepted?"ПОДТВЕРЖДЕНО":"не подтверждено"));
            Preview(t.OwnItems,t.OwnMaterials);
            Text("Предложение партнёра: "+(t.PartnerAccepted?"ПОДТВЕРЖДЕНО":"не подтверждено"));
            Preview(t.PartnerItems,t.PartnerMaterials);
            Text("Выберите до 4 неснаряжённых вещей и до 4 видов материалов:");
            foreach(var i in _inventory.Items??Array.Empty<InventoryEntry>())
            {
                if(i.Equipped)continue;
                var handle=i.Handle;var box=new CheckBox(){Text=i.Name,ButtonPressed=_selected.Contains(handle),Disabled=_waiting};
                box.Toggled+=on=>{if(on&&_selected.Count<4)_selected.Add(handle);else _selected.Remove(handle);box.SetPressedNoSignal(_selected.Contains(handle));};_body.AddChild(box);
            }
            foreach(var m in _materials.Materials??Array.Empty<MaterialAmount>())
            {
                var id=m.Id;Text("Материал "+id+" (доступно "+m.Quantity+")");
                var count=new SpinBox(){MinValue=0,MaxValue=m.Quantity,Step=1,Value=Math.Min(_quantities.GetValueOrDefault(id),m.Quantity),Editable=!_waiting};
                count.ValueChanged+=v=>{if(v==0)_quantities.Remove(id);else if(_quantities.ContainsKey(id)||_quantities.Count<4)_quantities[id]=(ushort)v;else count.SetValueNoSignal(0);};_body.AddChild(count);
            }
            Button("Отправить выбранное предложение",()=>Send(TradeAction.Offer,t.PartnerId,t.SessionId,t.Revision,_selected.Order().ToArray(),_quantities.Where(q=>q.Value>0).Select(q=>new MaterialAmount(q.Key,q.Value)).ToArray()));
            Button("Подтвердить показанное предложение",()=>Send(TradeAction.Accept,t.PartnerId,t.SessionId,t.Revision),t.OwnAccepted);
        }
        Button("Отменить обмен",()=>Send(TradeAction.Cancel,t.PartnerId,t.SessionId,t.Revision));
    }
    private void Preview(IReadOnlyList<TradeItem> items,IReadOnlyList<MaterialAmount> materials)
    {
        if(items.Count+materials.Count==0)Text("Пусто");
        foreach(var i in items)Text(i.Name+" | ATK +"+i.Attack+" DEF +"+i.Defense+" HP +"+i.Health+(i.Maximum>0?" | прочность "+i.Current+"/"+i.Maximum:""));
        foreach(var m in materials)Text("Материал "+m.Id+" × "+m.Quantity);
    }
}
