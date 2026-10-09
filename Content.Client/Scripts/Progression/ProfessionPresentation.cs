using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Progression;
public partial class ProfessionPresentation : CanvasLayer
{
    private readonly Label _summary=new() { Position=new(24,326),MouseFilter=Control.MouseFilterEnum.Ignore };
    private readonly UiWindow _panel=new() { ToggleKey=Key.P };
    private readonly VBoxContainer _rows=new();
    private NetworkClient _network=null!;
    private ProfessionState _state;
    private uint _sequence,_pending,_confirmation;
    private double _sentAt;
    private bool _alive=true;
    private string _feedback="";
    public void Initialize(NetworkClient network)
    { _network=network; Layer=24; AddChild(_summary); _summary.Hide(); AddChild(_panel); _panel.Build("Профессия",new(920,656)); var scroll=new ScrollContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled }; _panel.Body.AddChild(scroll); scroll.AddChild(_rows); _rows.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill; _panel.CloseRequested+=Close; }
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
        if (ev is not InputEventKey { Pressed:true,Echo:false,PhysicalKeycode:Key.P }) return;
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
    private Button Button(string text,Action action)
    { var b=new Button { Text=text,Disabled=_pending != 0 || !_alive }; b.Pressed+=action; return b; }
    private void Rebuild()
    {
        if (!_state.OwnerId.IsValid) return;
        foreach (var node in _rows.GetChildren()) { _rows.RemoveChild(node); node.QueueFree(); }
        var current=_state.ActiveId == 0 ? "Без профессии" : _state.ActiveName;
        _summary.Text=$"{current} [P]"+(_state.OfferedId == 0 ? "" : " • открылась возможность");
        _rows.AddChild(new Label { Text="Профессия [P]" });
        _rows.AddChild(new Label { Text=$"Сейчас: {current}" });
        _rows.AddChild(new Label { Text=_feedback });
        if (_state.TrainingRequired > 0 && _state.ActiveId != 2)
            _rows.AddChild(new Label { Text=$"Обучение мечу: {Math.Floor(_state.TrainingDamage):0} / {_state.TrainingRequired:0} урона\n" +
                (_state.TrainingDamage >= _state.TrainingRequired ? "Вернитесь к Радану на тренировочной арене." : "Наденьте тренировочный меч (I) и атакуйте манекены (ЛКМ).") });
        if (_state.ActiveId == 2)
        {
            UiComposition.Paragraph(_rows,"Мечник · пассивки действуют с надетым исправным мечом",18);
            UiComposition.Paragraph(_rows,"Владение мечом — +10% урона обычных атак.\nБоевой ритм — каждое третье попадание возвращает 6 выносливости; промах или пауза свыше 4 с сбивают ритм.\nКрепкий хват — после блока ещё −15% от оставшегося физического урона.\nРабота ног — +10% скорости бега на 1,5 с после прямого попадания.\nСобранность — успешное парирование усиливает следующую попытку удара на 10% на 3 с.",14);
            UiComposition.Paragraph(_rows,"Space — длинный рывок до 20 м: 35 выносливости, перезарядка 7 с, без неуязвимости.\nРеген: 8/с после 1 с; во время приёма и блока приостановлен.\nВсе 10 приёмов доступны в K → Навыки; на панели — выбранные восемь.",14);
        }
        if (_state.OfferedId == 0) { _rows.AddChild(new Label { Text="Мир замечает ваши действия." }); return; }
        _rows.AddChild(new Label { Text=$"Открыта возможность: {_state.OfferedName}" + (_state.OfferedId == 2 ? "\nДля принятия оставайтесь рядом с тренером." : "") });
        if (_confirmation == 0) { _rows.AddChild(Button("Рассмотреть переход",()=>Send(ProfessionAction.Prepare,_state.OfferedId))); return; }
        _rows.AddChild(new Label { Text=$"{current} → {_state.OfferedName}\nОдна активная профессия. Возврат к прежней\nпрофессии после перехода невозможен." });
        var acknowledge=new CheckBox { Text="Я понимаю необратимость перехода",Disabled=_pending != 0 || !_alive };
        _rows.AddChild(acknowledge);
        var confirm=Button("Подтвердить необратимый переход",()=> { if (acknowledge.ButtonPressed) Send(ProfessionAction.Confirm,_state.OfferedId,_confirmation); });
        confirm.Disabled=true; acknowledge.Toggled+=yes=>confirm.Disabled=!yes || _pending != 0 || !_alive;
        _rows.AddChild(confirm); _rows.AddChild(Button("Отмена",Cancel));
    }
}
