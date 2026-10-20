using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Progression;

public partial class RespecPresentation:CanvasLayer
{
    private NetworkClient _network=null!;
    private PlayerController _player=null!;
    private UiWindow _window=null!;
    private RespecView _view=null!;
    private QuestNpcSpawn _npc;
    private RespecQuote? _quote;
    private RespecCommand? _pending;
    private double _clock,_expires,_sent,_refresh;
    private bool _completed;
    private string _status="";
    public void Initialize(NetworkClient network,PlayerController player)
    {
        _network=network;_player=player;Layer=23;
        _window=new();AddChild(_window);_window.Build("Сброс характеристик",new(1080,650));_window.CloseRequested+=Close;
        _view=GD.Load<PackedScene>("res://UI/Screens/RespecView.tscn").Instantiate<RespecView>();_window.Body.AddChild(_view);
        _view.RefreshRequested+=RequestQuote;_view.ConfirmRequested+=Confirm;_view.CloseRequested+=Close;_view.AllocateRequested+=Allocate;
        network.RespecQuoteReceived+=Quote;network.RespecResultReceived+=Result;
    }
    public void Open(QuestNpcSpawn npc)
    {
        if(!_player.IsAlive||!_window.Open())return;_npc=npc;_quote=null;_completed=false;_status="";_view.SetQuote(null);
        if(_pending is not {Action:RespecAction.Confirm})RequestQuote();else Render();
    }
    private void Close()=>_window.Close();
    private void Allocate(){Close();GameUi.Navigate("character");}
    private void RequestQuote()
    {
        if(_pending is not null)return;
        _quote=null;_completed=false;_status="";_view.SetQuote(null);
        _pending=_network.NewRespecCommand(_npc.EntityId);Send();
    }
    private void Confirm()
    {
        if(_pending is not null||_quote is not {} q||_clock>=_expires)return;
        _pending=_network.NewRespecCommand(_npc.EntityId,q);Send();
    }
    private void Send(){_sent=_clock;_network.SendRespec(_pending!.Value);Render();}
    private void Quote(RespecQuote quote)
    {
        if(_pending is not {Action:RespecAction.Quote} request||request.Sequence!=quote.Sequence||quote.NpcId!=_npc.EntityId)return;
        _pending=null;_quote=quote;_expires=_clock+quote.ValidSeconds;_view.SetQuote(quote);_status="";Render();
    }
    private void Result(RespecResult result)
    {
        if(_pending?.Sequence!=result.Sequence)return;_pending=null;
        _completed=result.Outcome is RespecOutcome.Accepted or RespecOutcome.AlreadyProcessed;
        _status=result.Outcome switch
        {
            RespecOutcome.Accepted or RespecOutcome.AlreadyProcessed=>$"Возвращено очков: {result.ReturnedPoints}. "+(result.PaidCoins==0?"Первый сброс выполнен бесплатно.":$"Списано {result.PaidCoins} монет."),
            RespecOutcome.InCombat=>"Сначала выйдите из боя.",RespecOutcome.Busy=>"Завершите текущее действие или обмен и остановитесь.",
            RespecOutcome.TooFar=>"Подойдите ближе к трактирщику.",RespecOutcome.InvalidState=>"Сброс доступен живому персонажу.",
            RespecOutcome.NothingToReset=>"Вложенных очков пока нет. Сброс не выполнен, оплаты нет.",
            RespecOutcome.MigrationRequired=>"История распределения этого персонажа требует обновления сохранения.",
            RespecOutcome.StaleQuote=>"Данные изменились или срок расчёта истёк. Обновите расчёт.",
            RespecOutcome.InsufficientCoins=>"Недостаточно монет.",RespecOutcome.RateLimited=>"Слишком много запросов. Попробуйте ещё раз.",
            _=>"Эта услуга сейчас недоступна."
        };
        _quote=null;Render();
    }
    private void Render()
    {
        var ready=_quote is {} q&&_clock<_expires&&q.Coins>=q.Price&&_player.IsAlive&&_network.LatestDefense?.InCombatRemaining is not >0;
        var status=_pending is not null?(_pending.Value.Action==RespecAction.Quote?"Получаем расчёт…":"Сохраняем сброс…"):_status;
        if(_pending is null&&_quote is {} offer)
            status=_clock>=_expires?"Срок расчёта истёк. Обновите его.":offer.Coins<offer.Price?"Недостаточно монет.":$"Расчёт действителен ещё {Math.Ceiling(_expires-_clock)} с.";
        if(!_player.IsAlive)status="Сброс доступен живому персонажу.";
        else if(_network.LatestDefense?.InCombatRemaining>0)status="Сначала выйдите из боя.";
        _view.Render(ready,_pending is not null,_completed,status,_quote?.Price??0);
    }
    public override void _Process(double delta)
    {
        _clock+=delta;
        if(_pending is {} command&&_clock-_sent>=2){_sent=_clock;_network.SendRespec(command);}
        if(!_window.Visible)return;
        if(!_player.IsAlive){Close();return;}
        if(_clock>=_refresh){_refresh=_clock+.2;Render();}
    }
    public override void _ExitTree()
    {
        if(_network is null)return;_network.RespecQuoteReceived-=Quote;_network.RespecResultReceived-=Result;
        _view.RefreshRequested-=RequestQuote;_view.ConfirmRequested-=Confirm;_view.CloseRequested-=Close;_view.AllocateRequested-=Allocate;_window.CloseRequested-=Close;
    }
}
