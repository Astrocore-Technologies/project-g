using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;
using ProjectG.Gameplay;
namespace ProjectG.Economy;
public partial class EconomyPresentation:CanvasLayer
{
    private NetworkClient _network=null!;private PlayerController _player=null!;
    private UiWindow _panel=null!;
    private VBoxContainer _forgeItems=null!,_listings=null!,_sellItems=null!;
    private Control _workspace=null!,_confirmation=null!;
    private Label _coins=null!,_statusLabel=null!,_forgeName=null!,_forgeDescription=null!,_materialsLabel=null!,_quoteDetails=null!,_risk=null!;
    private TextureRect _forgeIcon=null!;
    private Button _evolve=null!,_enhance=null!,_sellOre=null!,_claim=null!,_confirm=null!;
    private SpinBox _priceInput=null!,_oreInput=null!;
    private ulong _selectedItem;
    private EconomyState _state;private InventoryState _inventory;private CraftState _materials;private MarketState _market;
    private readonly Dictionary<ulong,ItemConditionEntry> _conditions=new();
    private EconomyCommand? _pending;private EconomyQuote? _quote;private double _clock,_sentAt;private Vector2 _lastViewport;
    private string _status="Подойдите к верстаку (-10, 2).";private int _price=10,_oreQuantity=1;
    public void Initialize(NetworkClient network,PlayerController player)
    {
        _network=network;_player=player;Layer=7;
        _panel=new UiWindow { ToggleKey=Key.J }; AddChild(_panel); _panel.Build("Кузница и местный рынок",new(1080,650)); _panel.CloseRequested+=_panel.Close;
        var view=GD.Load<PackedScene>("res://UI/Screens/EconomyView.tscn").Instantiate<Control>(); _panel.Body.AddChild(view);
        _forgeItems=view.GetNode<VBoxContainer>("%ForgeItemsRows"); _listings=view.GetNode<VBoxContainer>("%ListingsRows"); _sellItems=view.GetNode<VBoxContainer>("%SellItemsRows");
        _workspace=view.GetNode<Control>("%Workspace"); _confirmation=view.GetNode<Control>("%Confirmation");
        _coins=view.GetNode<Label>("%Coins"); _statusLabel=view.GetNode<Label>("%Status"); _forgeName=view.GetNode<Label>("%ForgeName"); _forgeDescription=view.GetNode<Label>("%ForgeDescription");
        _materialsLabel=view.GetNode<Label>("%Materials"); _quoteDetails=view.GetNode<Label>("%QuoteDetails"); _risk=view.GetNode<Label>("%Risk"); _forgeIcon=view.GetNode<TextureRect>("%ForgeIcon");
        _evolve=view.GetNode<Button>("%Evolve"); _enhance=view.GetNode<Button>("%Enhance"); _sellOre=view.GetNode<Button>("%SellOre"); _claim=view.GetNode<Button>("%Claim"); _confirm=view.GetNode<Button>("%Confirm");
        _priceInput=view.GetNode<SpinBox>("%Price"); _oreInput=view.GetNode<SpinBox>("%OreQuantity");
        _priceInput.ValueChanged+=value=>_price=(int)value; _oreInput.ValueChanged+=value=>_oreQuantity=(int)value;
        _evolve.Pressed+=()=>Request(EconomyAction.Evolve,SelectedItem()); _enhance.Pressed+=()=>Request(EconomyAction.Enhance,SelectedItem());
        _sellOre.Pressed+=()=>Request(EconomyAction.SellOre,quantity:(ushort)_oreQuantity); _claim.Pressed+=()=>Request(EconomyAction.Claim); _confirm.Pressed+=Confirm;
        view.GetNode<Button>("%Cancel").Pressed+=()=> { _quote=null;_pending=null;_status="Расчёт отменён без списания.";Render(); };
        network.EconomyStateReceived+=State;network.EconomyQuoteReceived+=Quote;network.EconomyResultReceived+=Result;network.MarketStateReceived+=Market;
        network.InventoryReceived+=Inventory;network.CraftStateReceived+=Materials;network.ItemConditionReceived+=Condition;Render();
    }
    public override void _ExitTree(){if(_network is null)return;_network.EconomyStateReceived-=State;_network.EconomyQuoteReceived-=Quote;_network.EconomyResultReceived-=Result;_network.MarketStateReceived-=Market;_network.InventoryReceived-=Inventory;_network.CraftStateReceived-=Materials;_network.ItemConditionReceived-=Condition;}
    public override void _UnhandledInput(InputEvent e){if(PcInput.Pressed("market",e)){if(GameUi.GameplayModalOpen)return; Toggle();GetViewport().SetInputAsHandled();Render();}}
    public override void _Process(double delta)
    {
        _clock+=delta;
        if(_pending is {} c && _quote is null && _clock-_sentAt>=2){_sentAt=_clock;_network.SendEconomy(c);}
        if(_quote is {} q && _clock-_sentAt>=q.ValidSeconds){_quote=null;_pending=null;_status="Расчёт истёк. Запросите новый.";Render();}
    }
    public void Toggle() { if(_panel.Visible) _panel.Close(); else _panel.Open(); }
    private void State(EconomyState s){if(s.OwnerId!=_player.EntityId)return;_state=s;Render();}
    private void Inventory(InventoryState s){if(s.EntityId!=_player.EntityId)return;_inventory=s;Render();}
    private void Materials(CraftState s){if(s.OwnerId!=_player.EntityId)return;_materials=s;Render();}
    private void Condition(ItemConditionState s){if(s.OwnerId!=_player.EntityId)return;_conditions.Clear();foreach(var i in s.Items)_conditions[i.Handle]=i;Render();}
    private void Market(MarketState s){if(s.OwnerId!=_player.EntityId)return;_market=s;Render();}
    private void Quote(EconomyQuote q)
    {
        if(_pending is not {} c || c.QuoteId!=0 || q.Command with {QuoteId=0}!=c)return;
        _quote=q;_sentAt=_clock;_status="Проверьте цену и риск, затем подтвердите.";Render();
    }
    private void Result(EconomyResult r)
    {
        if(_pending?.Operation!=r.Operation)return;
        _pending=null;_quote=null;
        _status=r.Outcome is CraftOutcome.Accepted or CraftOutcome.AlreadyProcessed?"Результат: "+Effect(r.Effect):"Операция отклонена: "+Outcome(r.Outcome);Render();
    }
    private static string ActionName(EconomyAction a)=>a switch{EconomyAction.Evolve=>"развитие",EconomyAction.Enhance=>"заточка",EconomyAction.SellOre=>"продажа руды",EconomyAction.List=>"выставить вещь",EconomyAction.Buy=>"покупка",EconomyAction.Cancel=>"снять объявление",EconomyAction.Claim=>"получить выручку",_=>"операция"};
    internal static string Outcome(CraftOutcome o)=>o switch{CraftOutcome.Accepted=>"готово",CraftOutcome.AlreadyProcessed=>"уже выполнено",CraftOutcome.InvalidOperation=>"расчёт устарел или изменился",CraftOutcome.InvalidState=>"персонаж не может действовать",CraftOutcome.Busy=>"завершите движение, каст или обмен",CraftOutcome.TooFar=>"подойдите ближе",CraftOutcome.Blocked=>"путь к месту закрыт",CraftOutcome.Unavailable=>"действие сейчас недоступно",CraftOutcome.MissingMaterials=>"не хватает материалов или монет",CraftOutcome.InventoryFull=>"нет места в инвентаре или достигнут лимит объявлений",CraftOutcome.MaterialFull=>"достигнут лимит монет или выручки",CraftOutcome.RateLimited=>"слишком много действий",CraftOutcome.Cooldown=>"подождите",_=>"ресурс недоступен"};
    private static string Effect(EconomyEffect e)=>e switch{EconomyEffect.Evolved=>"оружие развито",EconomyEffect.Enhanced=>"заточка успешна",EconomyEffect.Failed=>"заточка не удалась, максимум прочности уменьшен",EconomyEffect.Destroyed=>"оружие уничтожено",EconomyEffect.SoldOre=>"руда продана",EconomyEffect.Listed=>"вещь выставлена",EconomyEffect.Bought=>"вещь куплена",EconomyEffect.Cancelled=>"вещь возвращена",EconomyEffect.Claimed=>"выручка получена",_=>"уже обработано"};
    private string MaterialName(ushort id)=>_network.CraftRecipes.Values.SelectMany(r=>r.Costs).FirstOrDefault(m=>m.Id==id).Name??("Материал "+id);
    private EconomyItem SelectedItem()=>(_state.Items??[]).FirstOrDefault(item=>item.Handle==_selectedItem);
    private void Request(EconomyAction action,EconomyItem item=default,ulong listing=0,ushort quantity=0,int price=0)
    {
        if(_pending is not null||_state.OwnerId!=_player.EntityId)return;
        var c=new EconomyCommand(_state.LastOperation+1,action,item.Handle,item.Revision,listing,quantity,price,0);_pending=c;_sentAt=_clock;_status="Запрашиваем цену и условия…";_network.SendEconomy(c);Render();
    }
    private void Confirm()
    {if(_quote is not {} q)return;_pending=q.Command;_quote=null;_sentAt=_clock;_status="Ожидаем сохранения результата…";_network.SendEconomy(q.Command);Render();}
    private void Render()
    {
        if(_forgeItems is null)return;
        foreach(var list in new[]{_forgeItems,_listings,_sellItems}) UiRows.Clear(list);
        var busy=_pending is not null || !_player.IsAlive || _state.OwnerId!=_player.EntityId;
        _statusLabel.Text=_status; _coins.Text=_state.OwnerId==_player.EntityId?$"Монеты: {_state.Coins:N0}   ·   Выручка: {_market.Credit:N0}":"Сведения загружаются…";
        _workspace.Visible=_quote is null; _confirmation.Visible=_quote is not null;
        _priceInput.Editable=!busy; _oreInput.Editable=!busy; _sellOre.Disabled=busy; _claim.Disabled=busy || _market.Credit==0;
        _materialsLabel.Text=(_materials.Materials??[]).Count>0?"Материалы: "+string.Join(", ",(_materials.Materials??[]).Select(m=>MaterialName(m.Id)+" × "+m.Quantity)):"Материалов пока нет";
        var eligible=(_inventory.Items??[]).Where(item=>(_state.Items??[]).Any(info=>info.Handle==item.Handle)&&_conditions.ContainsKey(item.Handle)).ToArray();
        if(!eligible.Any(item=>item.Handle==_selectedItem)) _selectedItem=eligible.FirstOrDefault().Handle;
        foreach(var item in eligible)
        {
            var selected=item; var condition=_conditions[item.Handle];
            UiRows.Action(_forgeItems,item.Name,$"Прочность {condition.Current}/{condition.Maximum}",UiAssets.EquipmentKey(item.Slot),item.Handle==_selectedItem?"Выбрано":"Выбрать",()=> { _selectedItem=selected.Handle; Render(); },busy);
        }
        var current=eligible.FirstOrDefault(item=>item.Handle==_selectedItem); var info=SelectedItem();
        _forgeName.Text=current.Handle==0?"Нет оружия для кузницы":current.Name;
        _forgeDescription.Text=current.Handle==0?"Получите оружие, чтобы увидеть доступные действия.":$"Атака +{current.AttackBonus:0.##} · Защита +{current.DefenseBonus:0.##}\nПрочность {_conditions[current.Handle].Current}/{_conditions[current.Handle].Maximum}";
        _forgeIcon.Texture=UiAssets.Texture(current.Handle==0?"equipment.unknown":UiAssets.EquipmentKey(current.Slot));
        _evolve.Disabled=busy || current.Handle==0; _enhance.Disabled=busy || current.Handle==0 || info.Enhancement>=5;
        if(eligible.Length==0) UiRows.Note(_forgeItems,"Подходящих предметов пока нет.");
        foreach(var item in _inventory.Items??[])
        {
            if(item.Equipped)continue; var sellInfo=(_state.Items??[]).FirstOrDefault(value=>value.Handle==item.Handle);
            if(sellInfo.Handle==0)continue;
            UiRows.Action(_sellItems,item.Name,"Не экипировано",UiAssets.EquipmentKey(item.Slot),"Выставить",()=>Request(EconomyAction.List,sellInfo,price:_price),busy);
        }
        if(_sellItems.GetChildCount()==0) UiRows.Note(_sellItems,"Нет неснаряжённых вещей для продажи.");
        foreach(var listing in _market.Listings??[])
        {
            var currentListing=listing; var item=listing.Item;
            UiRows.Action(_listings,item.Name,$"Атака +{item.Attack} · Защита +{item.Defense} · HP +{item.Health}\n{listing.Price:N0} монет"+(item.Maximum>0?$" · Прочность {item.Current}/{item.Maximum}":""),UiAssets.EquipmentKey(item.Slot),listing.Own?"Снять":"Рассчитать покупку",()=>Request(currentListing.Own?EconomyAction.Cancel:EconomyAction.Buy,listing:currentListing.Id),busy);
        }
        if((_market.Listings??[]).Count==0) UiRows.Note(_listings,"Известных объявлений пока нет. Рынок доступен рядом с верстаком.");
        if(_quote is not {} quote)return;
        var text=new System.Text.StringBuilder(); text.AppendLine("Действие: "+ActionName(quote.Command.Action)+" → "+quote.OutputName);
        foreach(var cost in quote.Costs) text.AppendLine("Стоимость: "+MaterialName(cost.Id)+" × "+cost.Quantity);
        if(quote.Command.Action==EconomyAction.List) text.AppendLine("Цена продажи: "+quote.Command.Price+" монет. Без комиссии.");
        if(quote.CoinChange!=0) text.AppendLine("Монеты: "+(quote.CoinChange>0?"+":"")+quote.CoinChange);
        _risk.Text="";
        if(quote.Command.Action==EconomyAction.Enhance)
        {
            text.AppendLine("Шанс успеха: "+quote.Chance+"%. Успех: ATK +"+quote.AttackGain+". LUK не влияет.");
            var condition=_conditions.GetValueOrDefault(quote.Command.ItemHandle);
            _risk.Text="Провал: максимум прочности −"+quote.DurabilityLoss+". Ремонт его не вернёт. При максимуме 0 оружие уничтожается.";
            if(condition.Maximum<=quote.DurabilityLoss) _risk.Text+="\nВНИМАНИЕ: провал этой попытки уничтожит оружие.";
        }
        else if(quote.AttackGain>0) text.AppendLine("ATK +"+quote.AttackGain+"; экземпляр и прочность сохраняются.");
        _quoteDetails.Text=text.ToString(); _confirm.Disabled=!_player.IsAlive;
    }
}
