using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;
using ProjectG.Gameplay;
namespace ProjectG.Trading;
public partial class TradePresentation:CanvasLayer
{
    private NetworkClient _network=null!;
    private PlayerController _player=null!;
    private UiWindow _panel=null!;
    private VBoxContainer _players=null!,_ownItems=null!,_partnerItems=null!,_selections=null!;
    private Control _playersPanel=null!,_offers=null!,_invitation=null!,_active=null!;
    private Label _status=null!,_phase=null!,_invitationText=null!,_ownTitle=null!,_partnerTitle=null!;
    private Button _acceptInvite=null!,_offer=null!,_accept=null!,_cancel=null!;
    private string _feedback="";
    private InventoryState _inventory;
    private CraftState _materials;
    private TradeState? _trade;
    private uint _sequence;
    private bool _waiting;
    private double _clock,_waitingSince;
    private bool _invitedByMe;
    private readonly HashSet<ulong> _selected=new();
    private readonly Dictionary<ushort,ushort> _quantities=new();
    public void Initialize(NetworkClient network,PlayerController player)
    {
        _network=network; _player=player; Layer=6;
        _panel=new UiWindow { ToggleKey=Key.B }; AddChild(_panel); _panel.Build("Обмен с игроком",new(1080,650)); _panel.CloseRequested+=_panel.Close;
        var view=GD.Load<PackedScene>("res://UI/Screens/TradeView.tscn").Instantiate<Control>(); _panel.Body.AddChild(view);
        _players=view.GetNode<VBoxContainer>("%PlayersRows"); _ownItems=view.GetNode<VBoxContainer>("%OwnItemsRows");
        _partnerItems=view.GetNode<VBoxContainer>("%PartnerItemsRows"); _selections=view.GetNode<VBoxContainer>("%SelectionsRows");
        _playersPanel=view.GetNode<Control>("%PlayersPanel"); _offers=view.GetNode<Control>("%Offers");
        _invitation=view.GetNode<Control>("%Invitation"); _active=view.GetNode<Control>("%Active");
        _status=view.GetNode<Label>("%Status"); _phase=view.GetNode<Label>("%Phase"); _invitationText=view.GetNode<Label>("%InvitationText");
        _ownTitle=view.GetNode<Label>("%OwnTitle"); _partnerTitle=view.GetNode<Label>("%PartnerTitle");
        _acceptInvite=view.GetNode<Button>("%AcceptInvite"); _accept=view.GetNode<Button>("%Accept"); _cancel=view.GetNode<Button>("%Cancel"); _offer=view.GetNode<Button>("%Offer");
        _acceptInvite.Pressed+=()=>Current(TradeAction.Accept); _accept.Pressed+=()=>Current(TradeAction.Accept); _cancel.Pressed+=()=>Current(TradeAction.Cancel);
        _offer.Pressed+=()=> { if(_trade is {} t) Send(TradeAction.Offer,t.PartnerId,t.SessionId,t.Revision,_selected.Order().ToArray(),_quantities.Where(q=>q.Value>0).Select(q=>new MaterialAmount(q.Key,q.Value)).ToArray()); };
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
    public override void _UnhandledInput(InputEvent e)
    { if(PcInput.Pressed("trade",e)) { if(GameUi.GameplayModalOpen)return; Toggle(); GetViewport().SetInputAsHandled(); Render(); } }
    public void Toggle() { if(_panel.Visible) _panel.Close(); else _panel.Open(); }
    private void Inventory(InventoryState s){if(s.EntityId!=_player.EntityId)return; _inventory=s; _selected.RemoveWhere(h=>!s.Items.Any(i=>i.Handle==h&&!i.Equipped));Render();}
    private void Materials(CraftState s){if(s.OwnerId!=_player.EntityId)return;_materials=s;Render();}
    private void Spawn(PlayerSpawn s){Render();}
    private void Despawn(PlayerDespawn s){Render();}
    private void State(TradeState s)
    {
        if(s.OwnerId!=_player.EntityId)return;
        _trade=s; _waiting=false; _panel.Open();
        if(s.Phase is TradePhase.Completed or TradePhase.Cancelled){_selected.Clear();_quantities.Clear();_invitedByMe=false;}
        Render();
    }
    private void Result(TradeResult r){if(r.Sequence!=_sequence)return;_waiting=false;if(r.Outcome!=CraftOutcome.Accepted && (_trade is null || _trade.Value.Phase is TradePhase.Completed or TradePhase.Cancelled))_invitedByMe=false;_feedback="Ответ сервера: "+ProjectG.Economy.EconomyPresentation.Outcome(r.Outcome);Render();}
    private void Send(TradeAction action,NetworkEntityId partner,ulong session,uint revision,IReadOnlyList<ulong>? items=null,IReadOnlyList<MaterialAmount>? materials=null)
    {
        if(_waiting)return;
        _waiting=true; _waitingSince=_clock; _feedback="";
        if(action==TradeAction.Invite)_invitedByMe=true;
        _network.SendTrade(new(++_sequence,action,session,partner,revision,items??Array.Empty<ulong>(),materials??Array.Empty<MaterialAmount>()));
        Render();
    }
    private void Current(TradeAction action)
    { if(_trade is {} trade) Send(action,trade.PartnerId,trade.SessionId,trade.Revision); }
    public override void _Process(double delta)
    {
        _clock+=delta;
        if(!_waiting || _clock-_waitingSince<12)return;
        _waiting=false;_feedback="Нет ответа сервера. Проверьте соединение и состояние обмена перед повтором.";Render();
    }
    private string MaterialName(ushort id)=>_network.CraftRecipes.Values.SelectMany(r=>r.Costs).FirstOrDefault(c=>c.Id==id).Name??"Материал";
    private void Render()
    {
        if(_players is null)return;
        foreach(var list in new[]{_players,_ownItems,_partnerItems,_selections}) UiRows.Clear(list);
        _status.Text=_waiting?"Ожидание сервера…":_feedback.Length>0?_feedback:"Предложение меняется только после ответа сервера.";
        var ongoing=_trade is {} open && open.Phase is not (TradePhase.Completed or TradePhase.Cancelled);
        _playersPanel.Visible=!ongoing; _offers.Visible=ongoing;
        _phase.Text=_trade is {} phase?phase.Phase switch {TradePhase.Completed=>"Обмен завершён",TradePhase.Cancelled=>"Обмен отменён",TradePhase.Invited=>"Приглашение к обмену",_=>"Проверьте обе стороны обмена"}:"Обмен с игроком";
        if(!ongoing)
        {
            var players=_network.KnownPlayers.Values.Where(p=>p.EntityId!=_player.EntityId).OrderBy(p=>p.PlayerId.Value).ToArray();
            foreach(var player in players) { var id=player.EntityId; UiRows.Action(_players,"Игрок "+player.PlayerId.Value,"Партнёр должен стоять рядом.","nav.character","Пригласить",()=>Send(TradeAction.Invite,id,0,0),_waiting); }
            if(players.Length==0) UiRows.Note(_players,"Рядом нет доступных игроков. Подойдите к партнёру, чтобы начать обмен.");
            return;
        }
        var t=_trade!.Value; var invited=t.Phase==TradePhase.Invited;
        _invitation.Visible=invited; _active.Visible=!invited;
        _invitationText.Text=_invitedByMe?"Приглашение отправлено. Ожидаем согласия партнёра.":"Игрок предлагает обмен. Проверьте предложения перед подтверждением.";
        _acceptInvite.Visible=!_invitedByMe; _acceptInvite.Disabled=_waiting; _cancel.Disabled=_waiting;
        _ownTitle.Text="Ваше предложение"+(t.OwnAccepted?" · подтверждено":"");
        _partnerTitle.Text="Предложение партнёра"+(t.PartnerAccepted?" · подтверждено":"");
        Preview(_ownItems,t.OwnItems,t.OwnMaterials); Preview(_partnerItems,t.PartnerItems,t.PartnerMaterials);
        _offer.Disabled=_waiting; _accept.Disabled=_waiting || t.OwnAccepted;
        if(invited)return;
        foreach(var item in _inventory.Items??Array.Empty<InventoryEntry>())
        {
            if(item.Equipped)continue;
            var handle=item.Handle; CheckBox? box=null;
            box=UiRows.Selection(_selections,item.Name,_selected.Contains(handle),_waiting,on=>
            { if(on&&_selected.Count<4)_selected.Add(handle);else _selected.Remove(handle);box!.SetPressedNoSignal(_selected.Contains(handle)); });
        }
        foreach(var material in _materials.Materials??Array.Empty<MaterialAmount>())
        {
            var id=material.Id; SpinBox? amount=null;
            amount=UiRows.Amount(_selections,MaterialName(id)+" · доступно "+material.Quantity,material.Quantity,Math.Min(_quantities.GetValueOrDefault(id),material.Quantity),!_waiting,value=>
            { if(value==0)_quantities.Remove(id);else if(_quantities.ContainsKey(id)||_quantities.Count<4)_quantities[id]=(ushort)value;else amount!.SetValueNoSignal(0); });
        }
        if(_selections.GetChildCount()==0) UiRows.Note(_selections,"Нет неснаряжённых вещей или материалов.");
    }
    private void Preview(Node parent,IReadOnlyList<TradeItem> items,IReadOnlyList<MaterialAmount> materials)
    {
        if(items.Count+materials.Count==0) UiRows.Note(parent,"Предложение пусто");
        foreach(var item in items) UiRows.Action(parent,item.Name,$"Атака +{item.Attack} · Защита +{item.Defense} · HP +{item.Health}"+(item.Maximum>0?$"\nПрочность {item.Current}/{item.Maximum}":""),UiAssets.EquipmentKey(item.Slot));
        foreach(var material in materials) UiRows.Note(parent,MaterialName(material.Id)+" × "+material.Quantity);
    }
}
