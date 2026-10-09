using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;
using ProjectG.Gameplay;
namespace ProjectG.Economy;
public partial class EconomyPresentation:CanvasLayer
{
    private NetworkClient _network=null!;private PlayerController _player=null!;
    private UiWindow _panel=null!;private VBoxContainer _body=null!;
    private EconomyState _state;private InventoryState _inventory;private CraftState _materials;private MarketState _market;
    private readonly Dictionary<ulong,ItemConditionEntry> _conditions=new();
    private EconomyCommand? _pending;private EconomyQuote? _quote;private double _clock,_sentAt;private Vector2 _lastViewport;
    private string _status="Подойдите к верстаку (-10, 2).";private int _price=10,_oreQuantity=1;
    public void Initialize(NetworkClient network,PlayerController player)
    {
        _network=network;_player=player;Layer=7;
        _panel=new UiWindow { ToggleKey=Key.J }; AddChild(_panel); _panel.Build("Кузница и местный рынок",new(1080,650)); _panel.CloseRequested+=_panel.Close;
        _body=UiComposition.Scroll(_panel.Body);
        network.EconomyStateReceived+=State;network.EconomyQuoteReceived+=Quote;network.EconomyResultReceived+=Result;network.MarketStateReceived+=Market;
        network.InventoryReceived+=Inventory;network.CraftStateReceived+=Materials;network.ItemConditionReceived+=Condition;Render();
    }
    public override void _ExitTree(){if(_network is null)return;_network.EconomyStateReceived-=State;_network.EconomyQuoteReceived-=Quote;_network.EconomyResultReceived-=Result;_network.MarketStateReceived-=Market;_network.InventoryReceived-=Inventory;_network.CraftStateReceived-=Materials;_network.ItemConditionReceived-=Condition;}
    public override void _UnhandledKeyInput(InputEvent e){if(e is InputEventKey{Pressed:true,Echo:false,PhysicalKeycode:Key.J}){if(GameUi.GameplayModalOpen)return; Toggle();GetViewport().SetInputAsHandled();Render();}}
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
    private void Text(string text)=>_body.AddChild(new Label(){Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart});
    private void Button(string text,Action action,bool disabled=false){var b=new Button(){Text=text,Disabled=disabled||_pending is not null||!_player.IsAlive};b.Pressed+=action;_body.AddChild(b);}
    private void Request(EconomyAction action,EconomyItem item=default,ulong listing=0,ushort quantity=0,int price=0)
    {
        if(_pending is not null||_state.OwnerId!=_player.EntityId)return;
        var c=new EconomyCommand(_state.LastOperation+1,action,item.Handle,item.Revision,listing,quantity,price,0);_pending=c;_sentAt=_clock;_status="Запрашиваем цену и условия…";_network.SendEconomy(c);Render();
    }
    private void Confirm()
    {if(_quote is not {} q)return;_pending=q.Command;_quote=null;_sentAt=_clock;_status="Ожидаем сохранения результата…";_network.SendEconomy(q.Command);Render();}
    private void Render()
    {
        if(_body is null)return;foreach(var c in _body.GetChildren()){_body.RemoveChild(c);c.QueueFree();}
        Text("Кузница и местный рынок — J | верстак (-10, 2)");Text("Монеты: "+_state.Coins+" | Выручка к получению: "+_market.Credit);Text(_status);if(_state.LastOperation>0)Text("Последняя сохранённая операция: "+Effect(_state.LastEffect));
        if(_quote is {} q)
        {
            Text("Действие: "+ActionName(q.Command.Action)+" → "+q.OutputName);
            foreach(var cost in q.Costs)Text("Стоимость: "+MaterialName(cost.Id)+" × "+cost.Quantity);
            if(q.Command.Action==EconomyAction.List)Text("Цена продажи: "+q.Command.Price+" монет. Без комиссии.");
            if(q.CoinChange!=0)Text("Монеты: "+(q.CoinChange>0?"+":"")+q.CoinChange);
            if(q.Command.Action==EconomyAction.Enhance)
            {
                Text("Шанс успеха: "+q.Chance+"%. Успех: ATK +"+q.AttackGain+". LUK не влияет.");
                var condition=_conditions.GetValueOrDefault(q.Command.ItemHandle);
                Text("Провал: максимум прочности −"+q.DurabilityLoss+". Ремонт его не вернёт. При максимуме 0 оружие уничтожается.");
                if(condition.Maximum<=q.DurabilityLoss)Text("ВНИМАНИЕ: провал этой попытки уничтожит оружие.");
            }
            else if(q.AttackGain>0)Text("ATK +"+q.AttackGain+"; экземпляр и прочность сохраняются.");
            var confirm=new Button(){Text="Подтвердить цену и последствия",Disabled=!_player.IsAlive};confirm.Pressed+=Confirm;_body.AddChild(confirm);
            var cancel=new Button(){Text="Отменить расчёт"};cancel.Pressed+=()=>{_quote=null;_pending=null;_status="Расчёт отменён без списания.";Render();};_body.AddChild(cancel);return;
        }
        Text("Материалы: "+string.Join(", ",(_materials.Materials??[]).Select(m=>MaterialName(m.Id)+" × "+m.Quantity)));
        Text("Оружие (эволюция и заточка сохраняют этот экземпляр):");
        foreach(var i in _inventory.Items??[])
        {
            var info=(_state.Items??[]).FirstOrDefault(s=>s.Handle==i.Handle);if(info.Handle==0)continue;
            if(_conditions.TryGetValue(i.Handle,out var condition))
            {Text(i.Name+" | прочность "+condition.Current+"/"+condition.Maximum);Button("Развить "+i.Name,()=>Request(EconomyAction.Evolve,info));Button("Заточить "+i.Name,()=>Request(EconomyAction.Enhance,info),info.Enhancement>=5);}
        }
        Text("Скупщик у верстака: 1 монета за единицу руды.");
        var amount=new SpinBox(){MinValue=1,MaxValue=999,Step=1,Value=_oreQuantity,Editable=_pending is null};amount.ValueChanged+=v=>_oreQuantity=(int)v;_body.AddChild(amount);
        Button("Рассчитать продажу руды",()=>Request(EconomyAction.SellOre,quantity:(ushort)_oreQuantity));
        Text("Выставить неснаряжённую вещь. Цена в игровых монетах:");
        var price=new SpinBox(){MinValue=1,MaxValue=1000000,Step=1,Value=_price,Editable=_pending is null};price.ValueChanged+=v=>_price=(int)v;_body.AddChild(price);
        foreach(var i in _inventory.Items??[])
        {if(i.Equipped)continue;var info=(_state.Items??[]).FirstOrDefault(s=>s.Handle==i.Handle);if(info.Handle>0)Button("Выставить "+i.Name,()=>Request(EconomyAction.List,info,price:_price));}
        Text("Местный рынок: до 2 ваших объявлений, до 8 в регионе. Без комиссии.");
        foreach(var listing in _market.Listings??[])
        {
            var l=listing;var item=l.Item;Text(item.Name+" | ATK +"+item.Attack+" DEF +"+item.Defense+" HP +"+item.Health+(item.Maximum>0?" | прочность "+item.Current+"/"+item.Maximum:"")+" | "+l.Price+" монет");
            Button(l.Own?"Снять и вернуть вещь":"Рассчитать покупку",()=>Request(l.Own?EconomyAction.Cancel:EconomyAction.Buy,listing:l.Id));
        }
        Button("Рассчитать получение выручки",()=>Request(EconomyAction.Claim),_market.Credit==0);
    }
}
