using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Progression;
public partial class ProfessionPresentation : CanvasLayer
{
    private readonly UiWindow _panel=new() { ToggleKey=Key.P };
    private Label _current=null!,_feedbackLabel=null!,_training=null!,_known=null!,_offerName=null!,_warning=null!;
    private Control _offer=null!,_confirmationPanel=null!;
    private Button _prepare=null!,_confirm=null!;
    private CheckBox _acknowledge=null!;
    private NetworkClient _network=null!;
    private ProfessionState _state;
    private uint _sequence,_pending,_confirmation;
    private double _sentAt;
    private bool _alive=true;
    private string _feedback="";
    public void Initialize(NetworkClient network)
    {
        _network=network; Layer=24; AddChild(_panel); _panel.Build("Профессия",new(920,656)); _panel.CloseRequested+=Close;
        var view=GD.Load<PackedScene>("res://UI/Screens/ProfessionView.tscn").Instantiate<Control>(); _panel.Body.AddChild(view);
        _current=view.GetNode<Label>("%Current"); _feedbackLabel=view.GetNode<Label>("%Feedback"); _training=view.GetNode<Label>("%Training"); _known=view.GetNode<Label>("%KnownDetails");
        _offer=view.GetNode<Control>("%Offer"); _offerName=view.GetNode<Label>("%OfferName"); _confirmationPanel=view.GetNode<Control>("%Confirmation"); _warning=view.GetNode<Label>("%Warning");
        _prepare=view.GetNode<Button>("%Prepare"); _prepare.Pressed+=()=>Send(ProfessionAction.Prepare,_state.OfferedId);
        _acknowledge=view.GetNode<CheckBox>("%Acknowledge"); _acknowledge.Toggled+=_=>Rebuild();
        _confirm=view.GetNode<Button>("%Confirm"); _confirm.Pressed+=()=> { if(_acknowledge.ButtonPressed) Send(ProfessionAction.Confirm,_state.OfferedId,_confirmation); };
        view.GetNode<Button>("%Cancel").Pressed+=Cancel;
    }
    public void Apply(ProfessionState state) { if (_state.OfferedId != state.OfferedId) _confirmation=0; _state=state; Rebuild(); }
    public void ApplyAlive(bool alive) { if (_alive == alive) return; _alive=alive; if (!alive) _confirmation=0; Rebuild(); }
    public void Result(ProfessionResult result)
    {
        if (result.Sequence != _pending) return;
        _pending=0; _confirmation=result.Confirmation;
        _feedback=result.Outcome switch { ProfessionOutcome.Prepared=>"Решение ещё не принято",ProfessionOutcome.Accepted=>"Профессия сохранена",ProfessionOutcome.Cancelled=>"Переход отменён",ProfessionOutcome.InvalidConfirmation=>"Подтверждение истекло. Начните снова",ProfessionOutcome.Busy=>"Завершите действие",ProfessionOutcome.InvalidState=>"Недоступно в этом состоянии",_=>"Переход сейчас недоступен" };

        Rebuild();
    }
    public override void _Process(double delta)
    { if (_pending != 0 && Time.GetTicksMsec()/1000d-_sentAt > 10) { _pending=0; _confirmation=0; _feedback="Нет ответа — проверьте подключение"; Rebuild(); } }
    public override void _UnhandledInput(InputEvent ev)
    {
        if (!PcInput.Pressed("profession",ev)) return;
        if(GameUi.GameplayModalOpen) return; Toggle();
        GetViewport().SetInputAsHandled();
    }
    public void Toggle() { if(_panel.Visible) Close(); else _panel.Open(); }
    private void Close() { _panel.Close(); if(_confirmation!=0) Cancel(); }
    private void Send(ProfessionAction action,ushort id=0,uint confirmation=0)
    {
        if (_pending != 0 || (!_alive && action != ProfessionAction.Cancel)) return;
        if (++_sequence == 0) ++_sequence;
        _pending=_sequence; _sentAt=Time.GetTicksMsec()/1000d; _feedback="Ожидание сервера…";
        _network.SendProfession(new(_sequence,action,id,confirmation)); Rebuild();
    }
    private void Cancel() { _confirmation=0; Send(ProfessionAction.Cancel); }
    private void Rebuild()
    {
        if(!_state.OwnerId.IsValid) return;
        var current=_state.ActiveId==0?"Без профессии":_state.ActiveName;
        _current.Text=current; _feedbackLabel.Text=_feedback;
        _training.Visible=_state.TrainingRequired>0 && _state.ActiveId!=2;
        _training.Text=$"Обучение мечу: {Math.Floor(_state.TrainingDamage):0} / {_state.TrainingRequired:0} урона\n"+
            (_state.TrainingDamage>=_state.TrainingRequired?"Вернитесь к Радану на тренировочной арене.":"Наденьте тренировочный меч (I) и атакуйте манекены (ЛКМ).");
        _known.Text=_state.ActiveId==2?
            "Мечник · пассивки действуют с надетым исправным мечом\n\n"+
            "Владение мечом — +10% урона обычных атак.\nБоевой ритм — каждое третье попадание возвращает 6 выносливости; промах или пауза свыше 4 с сбивают ритм.\nКрепкий хват — после блока ещё −15% от оставшегося физического урона.\nРабота ног — +10% скорости бега на 1,5 с после прямого попадания.\nСобранность — успешное парирование усиливает следующую попытку удара на 10% на 3 с.\n\n"+
            "Space — длинный рывок до 20 м: 35 выносливости, перезарядка 7 с, без неуязвимости.\nРеген: 8/с после 1 с; во время приёма и блока приостановлен.\nВсе 10 приёмов доступны в K → Навыки; на панели — выбранные восемь.":
            "Новые сведения о профессиях откроются по мере пути.";
        var busy=_pending!=0 || !_alive;
        _offer.Visible=_state.OfferedId!=0; _offerName.Text="Открыта возможность: "+_state.OfferedName+(_state.OfferedId==2?"\nДля принятия оставайтесь рядом с тренером.":"");
        _prepare.Visible=_confirmation==0; _prepare.Disabled=busy;
        _confirmationPanel.Visible=_confirmation!=0;
        _warning.Text=$"{current} → {_state.OfferedName}\nОдна активная профессия. Возврат к прежней профессии после перехода невозможен.";
        if(_confirmation==0) _acknowledge.SetPressedNoSignal(false);
        _acknowledge.Disabled=busy; _confirm.Disabled=busy || !_acknowledge.ButtonPressed;
    }
}
