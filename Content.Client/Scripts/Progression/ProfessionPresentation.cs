using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
namespace ProjectG.Progression;
public partial class ProfessionPresentation : CanvasLayer
{
    private readonly Label _summary=new() { Position=new(16,40),MouseFilter=Control.MouseFilterEnum.Ignore };
    private readonly PanelContainer _panel=new() { Position=new(460,62),CustomMinimumSize=new(410,0),Visible=false };
    private readonly VBoxContainer _rows=new();
    private NetworkClient _network=null!;
    private ProfessionState _state;
    private uint _sequence,_pending,_confirmation;
    private double _sentAt;
    private bool _alive=true;
    private string _feedback="";
    public void Initialize(NetworkClient network)
    { _network=network; AddChild(_summary); AddChild(_panel); _panel.AddChild(_rows); }
    public void Apply(ProfessionState state) { if (_state.OfferedId != state.OfferedId) _confirmation=0; _state=state; Rebuild(); }
    public void ApplyAlive(bool alive) { if (_alive == alive) return; _alive=alive; if (!alive) _confirmation=0; Rebuild(); }
    public void Result(ProfessionResult result)
    {
        if (result.Sequence != _pending) return;
        _pending=0; _confirmation=result.Confirmation;
        _feedback=result.Outcome switch { ProfessionOutcome.Prepared=>"Решение ещё не принято",ProfessionOutcome.Accepted=>"Профессия сохранена",ProfessionOutcome.Cancelled=>"Переход отменён",ProfessionOutcome.InvalidConfirmation=>"Подтверждение истекло. Начните снова",ProfessionOutcome.Busy=>"Завершите действие",ProfessionOutcome.InvalidState=>"Недоступно в этом состоянии",_=>"Переход сейчас недоступен" };
        if (_confirmation != 0) _panel.Visible=true;
        Rebuild();
    }
    public override void _Process(double delta)
    { if (_pending != 0 && Time.GetTicksMsec()/1000d-_sentAt > 10) { _pending=0; _confirmation=0; _feedback="Нет ответа — проверьте подключение"; Rebuild(); } }
    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed:true,Echo:false,PhysicalKeycode:Key.P }) return;
        _panel.Visible=!_panel.Visible;
        if (!_panel.Visible && _confirmation != 0) Cancel();
        GetViewport().SetInputAsHandled();
    }
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
        if (_state.OfferedId == 0) { _rows.AddChild(new Label { Text="Мир замечает ваши действия." }); return; }
        _rows.AddChild(new Label { Text=$"Открыта возможность: {_state.OfferedName}" });
        if (_confirmation == 0) { _rows.AddChild(Button("Рассмотреть переход",()=>Send(ProfessionAction.Prepare,_state.OfferedId))); return; }
        _rows.AddChild(new Label { Text=$"{current} → {_state.OfferedName}\nОдна активная профессия. Возврат к прежней\nпрофессии после перехода невозможен." });
        var acknowledge=new CheckBox { Text="Я понимаю необратимость перехода",Disabled=_pending != 0 || !_alive };
        _rows.AddChild(acknowledge);
        var confirm=Button("Подтвердить необратимый переход",()=> { if (acknowledge.ButtonPressed) Send(ProfessionAction.Confirm,_state.OfferedId,_confirmation); });
        confirm.Disabled=true; acknowledge.Toggled+=yes=>confirm.Disabled=!yes || _pending != 0 || !_alive;
        _rows.AddChild(confirm); _rows.AddChild(Button("Отмена",Cancel));
    }
}
