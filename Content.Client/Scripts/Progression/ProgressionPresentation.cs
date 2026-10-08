using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
namespace ProjectG.Progression;

/// <summary>Displays committed owner state. Buttons send intentions, never optimistic stat/EXP changes.</summary>
public partial class ProgressionPresentation : CanvasLayer
{
    private readonly Label _summary = new() { Position = new(16,16), MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly PanelContainer _panel = new() { Position = new(16,62), CustomMinimumSize = new(420,0), Visible = false };
    private readonly VBoxContainer _rows = new();
    private NetworkClient _network = null!;
    private ProgressionState _state;
    private uint _sequence, _pending;
    private bool _alive = true;
    private string _feedback = "";
    private double _sentAt;
    private static readonly string[] Keys = ["Q","W","E","R","A","S","D","F"];
    private static string SkillName(ushort id) => id switch { 1 => "Снаряд",2 => "Область",3 => "Рывок",5 => "Болт открытия",6 => "Импульс",_ => "Навык" };
    private static readonly string[] StatNames = ["STR","AGI","VIT","INT","DEX","LUK"];
    public void Initialize(NetworkClient network)
    { _network = network; AddChild(_summary); AddChild(_panel); var scroll = new ScrollContainer { CustomMinimumSize = new(420,460) }; _panel.AddChild(scroll); scroll.AddChild(_rows); }
    public void Apply(ProgressionState state) { _state = state; Rebuild(); }
    public void ApplyAlive(bool alive) { _alive = alive; Rebuild(); }
    public void Result(ProgressionResult result)
    {
        if (result.Sequence != _pending) return;
        _pending = 0; _feedback = result.Outcome == ProgressionOutcome.Accepted ? "Сохранено" : result.Outcome.ToString(); Rebuild();
    }
    public override void _Process(double delta)
    {
        if (_pending != 0 && Time.GetTicksMsec()/1000d - _sentAt > 10)
        { _pending = 0; _feedback = "Нет ответа — проверьте подключение"; Rebuild(); }
    }
    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.K })
        { _panel.Visible = !_panel.Visible; GetViewport().SetInputAsHandled(); }
    }
    private void Send(ProgressionAction action, byte stat = 0, ushort skill = 0, byte slot = 0)
    {
        if (_pending != 0 || !_alive) return;
        if (++_sequence == 0) ++_sequence;
        _pending = _sequence; _sentAt = Time.GetTicksMsec()/1000d; _feedback = "Сохранение…";
        _network.SendProgression(new(_sequence,action,stat,skill,slot)); Rebuild();
    }
    private Button Button(string text, Action action, bool disabled = false)
    { var button = new Button { Text = text, Disabled = disabled || _pending != 0 || !_alive }; button.Pressed += action; return button; }
    private void Rebuild()
    {
        if (_state.Skills is null) return;
        foreach (var node in _rows.GetChildren()) { _rows.RemoveChild(node); node.QueueFree(); }
        _summary.Text = $"Ур. {_state.Level}   EXP {_state.Experience}/{(_state.NextExperience == 0 ? "MAX" : _state.NextExperience.ToString())}   Stat points: {_state.StatPoints}   [K]";
        _rows.AddChild(new Label { Text = "Прогрессия и навыки [K]" });
        _rows.AddChild(new Label { Text = "Открытия: (-9, -6) и (9, 6) • повторно EXP не дают" });
        _rows.AddChild(new Label { Text = _feedback });
        for (byte i=0;i<6;i++)
        {
            var index = i; var row = new HBoxContainer(); _rows.AddChild(row);
            row.AddChild(new Label { Text = $"{StatNames[i]}: {_state.Stats[i]:0}", CustomMinimumSize = new(100,0) });
            row.AddChild(Button("+1",() => Send(ProgressionAction.AllocateStat,index),_state.StatPoints == 0));
        }
        _rows.AddChild(new Label { Text = "Панель: Q W E R A S D F • рывок: Space" });
        foreach (var skill in _state.Skills)
        {
            var row = new HBoxContainer(); _rows.AddChild(row);
            row.AddChild(new Label { Text = skill.Level == 0 ? $"{SkillName(skill.Id)} (открытие)" : $"{SkillName(skill.Id)} ур.{skill.Level} · {skill.Practice}/{(skill.NextPractice == 0 ? "MAX" : skill.NextPractice.ToString())}", CustomMinimumSize = new(180,0) });
            if (skill.Level == 0)
            { row.AddChild(Button("Изучить",() => Send(ProgressionAction.LearnSkill,skill:skill.Id),!skill.Learnable)); continue; }
            // Dash is a tactical action outside the eight editable slots.
            if (skill.Id == 3) { row.AddChild(new Label { Text = "Space" }); continue; }
            var choices = new OptionButton(); choices.AddItem("Снять"); foreach (var key in Keys) choices.AddItem(key);
            choices.Select(skill.Slot); choices.Disabled = _pending != 0 || !_alive; row.AddChild(choices);
            row.AddChild(Button("Назначить",() => Send(ProgressionAction.AssignSlot,skill:skill.Id,slot:(byte)choices.Selected)));
        }
    }
}
