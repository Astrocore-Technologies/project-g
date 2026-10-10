using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Progression;

/// <summary>Local draft only. Server calculates previews and commits a complete allocation atomically.</summary>
public partial class ProgressionPresentation : CanvasLayer
{
    private NetworkClient _network = null!;
    private UiWindow _window=null!;
    private Control _overlay = null!;
    private VBoxContainer _skills = null!;
    private Label _points = null!, _feedback = null!;
    private readonly Label[] _stats = new Label[6], _derived = new Label[17];
    private readonly Button[] _plus = new Button[6], _minus = new Button[6];
    private Button _apply = null!, _cancel = null!;
    private readonly int[] _draft = new int[6];
    private ProgressionState _state;
    private StatPreview? _preview;
    private uint _sequence, _pending;
    private ProgressionAction _pendingAction;
    private int _version, _sentVersion, _previewVersion = -1;
    private bool _alive = true, _needsPreview;
    private double _sentAt, _previewAfter;
    private UiModelPreview _modelPreview=null!;
    // NC: a visual-only scene includes the body, face and hair without gameplay nodes.
    public void SetPreviewVisual(PackedScene appearance) => _modelPreview.SetVisual(appearance);
    private static readonly string[] Keys = ["Q","W","E","R","A","S","D","F"];
    private static readonly string[] StatNames = ["STR · Сила","AGI · Ловкость","VIT · Живучесть","INT · Интеллект","DEX · Сноровка","LUK · Удача"];
    private static readonly string[] Descriptions = [
        "Ближняя атака и усиление оружия. Небольшой вклад в дальнюю атаку.",
        "Скорость атак и немного физической защиты. Не меняет скорость бега.",
        "Здоровье, восстановление HP, лечебные предметы, физическая и магическая защита.",
        "Магическая атака, мана и её регенерация, mana-предметы, магическая защита. Не ускоряет каст.",
        "Дальняя атака и оружие, скорость каста. Вторичный вклад в атаки, скорость атак и магическую защиту.",
        "Шанс критического удара и вторичный вклад в физическую и магическую атаку."];
    private static readonly string[] DerivedNames = ["Ближняя атака","Дальняя атака","Магическая атака","Ближнее оружие","Дальнее оружие","Физ. защита","Маг. защита","Макс. HP","Макс. мана","Восстановление HP*","Реген маны /с","Лечебные предметы","Предметы маны","Скорость атак","Скорость каста","Шанс крита"];
    public void Initialize(NetworkClient network)
    {
        _network = network; Layer = 20;
        _network.StatPreviewReceived += Preview; _network.InventoryReceived += EquipmentChanged;
        _network.ItemConditionReceived += ConditionChanged;
        _window=new UiWindow { ToggleKey=Key.K }; AddChild(_window); _window.Build("Персонаж",new(1180,656)); _overlay=_window;
        _window.CloseRequested+=()=> { _window.Close(); GameUi.CharacterWindowOpen=false; };
        var body=UiComposition.Page(_window,"character");
        _points = GameUi.Text(""); body.AddChild(_points);
        var content=new HBoxContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; body.AddChild(content);
        var appearance=UiComposition.Card(content,270);
        appearance.AddChild(GameUi.Text("Облик персонажа",20));
        _modelPreview=new UiModelPreview { SizeFlagsVertical=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(240,240) }; appearance.AddChild(_modelPreview);
        UiComposition.Paragraph(appearance,"ЛКМ — поворот · колесо — масштаб",12);
        appearance.AddChild(GameUi.Button("Экипировка · I",()=>GameUi.Navigate("inventory")));
        appearance.AddChild(GameUi.Button("Профессия · P",()=>GameUi.Navigate("profession")));
        var tabs=new TabContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; content.AddChild(tabs);
        var scroll=new ScrollContainer { Name="Характеристики",HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled }; tabs.AddChild(scroll);
        var columns=new VBoxContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; scroll.AddChild(columns);
        var primary = new VBoxContainer { CustomMinimumSize = new(330,0) }; columns.AddChild(primary);
        primary.AddChild(GameUi.Text("БАЗОВЫЕ ХАРАКТЕРИСТИКИ",14));
        for (var i=0; i<6; i++)
        {
            var index = i; var row = new HBoxContainer { TooltipText = Descriptions[i] }; primary.AddChild(row);
            _stats[i] = GameUi.Text(""); _stats[i].SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(_stats[i]);
            _minus[i] = GameUi.Button("−",()=>Change(index,-1)); _minus[i].CustomMinimumSize = new(34,34); row.AddChild(_minus[i]);
            _plus[i] = GameUi.Button("+",()=>Change(index,1)); _plus[i].CustomMinimumSize = new(34,34); row.AddChild(_plus[i]);
        }
        var hint = GameUi.Text("Подсказки — при наведении на стат.\n\nЧерновик учитывает экипировку.\nОчки тратятся только по «Применить».\n\n* Пассивный реген HP пока не работает.",14);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; primary.AddChild(hint);
        var details = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; columns.AddChild(details);
        details.AddThemeConstantOverride("separation",2);
        details.AddChild(GameUi.Text("СЕЙЧАС → ПОСЛЕ ПРИМЕНЕНИЯ",14));
        for (var i=0;i<_derived.Length;i++) { _derived[i] = GameUi.Text((i==16 ? "Блок урона" : DerivedNames[i])+": —",14); details.AddChild(_derived[i]); }
        var skillScroll = new ScrollContainer { Name = "Навыки" }; tabs.AddChild(skillScroll);
        _skills = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; skillScroll.AddChild(_skills);
        _feedback = GameUi.Text("",14); body.AddChild(_feedback);
        var footer = new HBoxContainer(); body.AddChild(footer);
        _cancel = GameUi.Button("Отменить распределение",Cancel); footer.AddChild(_cancel);
        _apply = GameUi.Button("Применить",()=>Send(ProgressionAction.AllocateStats)); footer.AddChild(_apply);
        Refresh(); Layout();
    }
    public override void _ExitTree()
    { GameUi.CharacterWindowOpen=false; if (_network is not null) { _network.StatPreviewReceived -= Preview; _network.InventoryReceived -= EquipmentChanged; _network.ItemConditionReceived -= ConditionChanged; } }
    public void Apply(ProgressionState state)
    {
        var changed = _state.Stats is null || !_state.Stats.SequenceEqual(state.Stats);
        _state = state;
        if (changed || _draft.Sum() > state.StatPoints) Array.Clear(_draft);
        Invalidate(); RebuildSkills(); Refresh();
    }
    public void ApplyAlive(bool alive) { _alive = alive; Refresh(); }
    private void EquipmentChanged(InventoryState value) { if (value.EntityId == _state.OwnerId) Invalidate(); }
    private void ConditionChanged(ItemConditionState value) { if (value.OwnerId == _state.OwnerId) Invalidate(); }
    public void Result(ProgressionResult result)
    {
        if (result.Sequence != _pending) return;
        // Accepted preview follows its result on the same reliable ordered stream.
        if (_pendingAction == ProgressionAction.PreviewStats && result.Outcome == ProgressionOutcome.Accepted) return;
        var allocation = _pendingAction == ProgressionAction.AllocateStats; _pending = 0;
        if (result.Outcome == ProgressionOutcome.Accepted)
        { if (allocation) { Array.Clear(_draft); Invalidate(); } _feedback.Text = "Изменения сохранены"; }
        else _feedback.Text = result.Outcome switch { ProgressionOutcome.NoPoints => "Недостаточно очков", ProgressionOutcome.Busy => "Дождитесь завершения действия", ProgressionOutcome.RateLimited => "Слишком часто. Попробуйте ещё раз", ProgressionOutcome.InvalidState => "Сейчас действие недоступно", _ => "Действие недоступно" };
        Refresh(); RebuildSkills();
    }
    private void Preview(StatPreview value)
    {
        if (value.Sequence != _pending || _pendingAction != ProgressionAction.PreviewStats) return;
        _pending = 0;
        if (_sentVersion == _version) { _preview = value; _previewVersion = _version; _feedback.Text = "Предпросмотр готов · очки ещё не потрачены"; }
        Refresh(); RebuildSkills();
    }
    private void Invalidate() { _version++; _previewVersion = -1; _needsPreview = true; _previewAfter = Now()+.15; }
    private void Change(int index, int amount)
    {
        if (_pending != 0 && _pendingAction != ProgressionAction.PreviewStats) return;
        if (!_alive || _draft[index]+amount < 0 || _draft.Sum()+amount > _state.StatPoints) return;
        _draft[index] += amount; Invalidate(); Refresh();
    }
    private void Cancel() { Array.Clear(_draft); Invalidate(); Refresh(); }
    public void Toggle()
    {
        if(_window.Visible) { _window.RequestClose(); return; }
        if(_window.Open()) { GameUi.CharacterWindowOpen=true; Invalidate(); }
    }
    public override void _UnhandledInput(InputEvent ev)
    { if (ev is InputEventKey { Pressed:true,Echo:false,PhysicalKeycode:Key.K }) { Toggle(); GetViewport().SetInputAsHandled(); } }
    public override void _Process(double delta)
    {
        if (_pending != 0 && Now()-_sentAt > 10)
        { _pending = 0; _previewVersion = -1; _feedback.Text = "Нет ответа. Закройте и откройте окно для обновления"; Refresh(); RebuildSkills(); }
        if (_overlay.Visible) { Layout(); if (_needsPreview && _pending == 0 && _state.Stats is not null && Now() >= _previewAfter) { _needsPreview = false; Send(ProgressionAction.PreviewStats); } }
    }
    private void Layout() => _window.Layout();
    private void Send(ProgressionAction action, ushort skill = 0, byte slot = 0)
    {
        if (_pending != 0 || (!_alive && action != ProgressionAction.PreviewStats)) return;
        if (++_sequence == 0) ++_sequence;
        _pending = _sequence; _pendingAction = action; _sentAt = Now(); _sentVersion = _version;
        _feedback.Text = action == ProgressionAction.PreviewStats ? "Расчёт предпросмотра…" : "Сохранение…";
        var allocation = action is ProgressionAction.PreviewStats or ProgressionAction.AllocateStats ? (int[])_draft.Clone() : null;
        _network.SendProgression(new(_sequence,action,0,skill,slot,allocation)); Refresh(); RebuildSkills();
    }
    private void Refresh()
    {
        if (_points is null) return;
        var total = _draft.Sum(); var busy = _pending != 0 && _pendingAction != ProgressionAction.PreviewStats;
        _points.Text = $"Уровень {_state.Level}  ·  Свободно: {_state.StatPoints-total}  ·  В черновике: {total}";
        for (var i=0;i<6;i++)
        {
            _stats[i].Text = $"{StatNames[i]}  {(_state.Stats is null ? 0 : _state.Stats[i]):0}"+(_draft[i]>0 ? $" → {_state.Stats![i]+_draft[i]:0}" : "");
            _plus[i].Disabled = busy || !_alive || total >= _state.StatPoints; _minus[i].Disabled = busy || !_alive || _draft[i] == 0;
        }
        _apply.Disabled = !_alive || _pending != 0 || total == 0 || _previewVersion != _version; _cancel.Disabled = busy || total == 0;
        for (var i=0;i<_derived.Length;i++)
        {
            var current = _preview is { } p ? Format(i,p.Current[i]) : "—";
            var after = _previewVersion == _version && _preview is { } v ? Format(i,v.Projected[i]) : "…";
            _derived[i].Text = $"{(i==16 ? "Блок урона" : DerivedNames[i])}: {current}"+(total>0 ? $" → {after}" : "");
            _derived[i].Modulate = _previewVersion == _version && _preview is { } ready && ready.Projected[i] != ready.Current[i] ? GameUi.Accent : Colors.White;
        }
    }
    internal static string Format(int i, double value)
    {
        // Multipliers are displayed as bonuses relative to the unchanged 100% baseline.
        if (i is 3 or 4 or 11 or 12 or 13 or 14)
            return (value - 1).ToString("+0.#%;-0.#%;0%");
        return i is 15 or 16 ? $"{value*100:0.#}%" : $"{value:0}";
    }
    private void RebuildSkills()
    {
        if (_skills is null || _state.Skills is null) return;
        foreach (var node in _skills.GetChildren()) { _skills.RemoveChild(node); node.QueueFree(); }
        _skills.AddChild(GameUi.Text("8 ячеек навыков · рывок Space — отдельно"));
        foreach (var skill in _state.Skills.OrderByDescending(s => s.Id is >=20 and <=29))
        {
            var row = new VBoxContainer(); _skills.AddChild(row);
            var name = GameUi.Text(GameUi.SkillName(skill.Id)+(skill.Level == 0 ? " · не изучен" : $" · ур. {skill.Level} · освоение {skill.Practice}/{skill.NextPractice}"));
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; name.AutowrapMode=TextServer.AutowrapMode.WordSmart; row.AddChild(name);
            if (skill.Id is >= 20 and <= 29) UiComposition.Paragraph(row,"Требуется меч · " + SwordsmanUi.Description(skill.Id),14);
            if (skill.Id == 3) { row.AddChild(GameUi.Text("Space")); continue; }
            if (skill.Level == 0)
            { var learn = GameUi.Button("Изучить",()=>Send(ProgressionAction.LearnSkill,skill.Id)); learn.Disabled = !skill.Learnable || _pending != 0 || !_alive; row.AddChild(learn); continue; }
            var choices = new OptionButton(); choices.AddItem("Снять"); foreach (var key in Keys) choices.AddItem(key); choices.Select(skill.Slot); row.AddChild(choices); choices.Disabled = _pending != 0 || !_alive;
            var assign = GameUi.Button("Назначить",()=>Send(ProgressionAction.AssignSlot,skill.Id,(byte)choices.Selected)); assign.Disabled = _pending != 0 || !_alive; row.AddChild(assign);
        }
    }
    private static double Now() => Time.GetTicksMsec()/1000d;
}
