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
    private GridContainer _skills = null!;
    private UiCharacterDisplay _display=null!;
    private TabContainer _tabs=null!;
    private Control _draftActions=null!;
    private readonly Label[] _summary=new Label[11];
    private Label _skillTitle=null!,_skillDescription=null!;
    private OptionButton _skillSlot=null!;
    private Button _skillAction=null!;
    private ushort _selectedSkill;
    private Label _points = null!, _feedback = null!;
    private readonly Label[] _stats = new Label[7], _derived = new Label[NetworkConstants.CharacterStatCount];
    private readonly Button[] _plus = new Button[7], _minus = new Button[7];
    private Button _apply = null!, _cancel = null!;
    private readonly int[] _draft = new int[7];
    private ProgressionState _state;
    private StatPreview? _preview;
    private uint _sequence, _pending;
    private ProgressionAction _pendingAction;
    private int _version, _sentVersion, _previewVersion = -1;
    private bool _alive = true, _needsPreview;
    private double _sentAt, _previewAfter;
    // NC: a visual-only scene includes the body, face and hair without gameplay nodes.
    public void SetPreviewVisual(PackedScene appearance,Texture2D? portrait=null) => _display.SetCharacter(appearance,portrait);
    private static readonly string[] Keys = ["Q","W","E","R","A","S","D","F"];
    private static readonly string[] StatNames = ["STR · Сила","AGI · Ловкость","VIT · Живучесть","INT · Интеллект","DEX · Сноровка","LUK · Удача","WIS · Мудрость"];
    private static readonly string[] Descriptions = [
        "Ближняя атака и усиление оружия. Небольшой вклад в дальнюю атаку.",
        "Скорость атак, числовое уклонение и немного физической защиты. Не меняет скорость бега.",
        "Здоровье, восстановление HP, лечебные предметы и физическая защита.",
        "Магическая атака. Запас маны, регенерация и магическая защита зависят от WIS.",
        "Дальняя атака и оружие, скорость каста, точность. Вторичный вклад в атаки и скорость атак.",
        "Шанс критического удара и вторичный вклад в физическую и магическую атаку.",
        "Максимальный запас маны, регенерация маны и магическая защита."];
    private static readonly string[] DerivedNames = ["Ближняя атака","Дальняя атака","Магическая атака","Ближнее оружие","Дальнее оружие","Физ. защита","Маг. защита","Макс. HP","Макс. мана","Реген HP /с","Реген маны /с","Лечебные предметы","Предметы маны","Скорость атак","Скорость каста","Шанс крита","Блок урона","Точность","Уклонение","Критический урон","Прочность блока","Сокращение перезарядок","Макс. выносливость","Реген выносливости /с"];
    internal static string PrimaryStatName(int index)=>StatNames[index];
    internal static string DerivedStatName(int index)=>DerivedNames[index];
    public void Initialize(NetworkClient network)
    {
        _network = network; Layer = 20;
        _network.StatPreviewReceived += Preview; _network.InventoryReceived += EquipmentChanged;
        _network.ItemConditionReceived += ConditionChanged;
        _window=new UiWindow { ToggleKey=Key.K }; AddChild(_window); _window.Build("Персонаж",new(1180,656)); _overlay=_window;
        _window.CloseRequested+=()=> { _window.Close(); GameUi.CharacterWindowOpen=false; };
        var view=GD.Load<PackedScene>("res://UI/Screens/CharacterView.tscn").Instantiate<Control>(); _window.Body.AddChild(view);
        _display=view.GetNode<UiCharacterDisplay>("%Display"); _tabs=view.GetNode<TabContainer>("%Tabs");
        _points=view.GetNode<Label>("%Points"); _feedback=view.GetNode<Label>("%Feedback");
        _draftActions=view.GetNode<Control>("%DraftActions");
        for(var i=0;i<_summary.Length;i++) _summary[i]=view.GetNode<Label>("%Summary"+i);
        for(var i=0;i<7;i++)
        {
            var index=i; _stats[i]=view.GetNode<Label>("%Stat"+i); _stats[i].TooltipText=Descriptions[i];
            _plus[i]=view.GetNode<Button>("%Plus"+i); _minus[i]=view.GetNode<Button>("%Minus"+i);
            _plus[i].Pressed+=()=>Change(index,1); _minus[i].Pressed+=()=>Change(index,-1);
        }
        for(var i=0;i<_derived.Length;i++) _derived[i]=view.GetNode<Label>("%Derived"+i);
        _skills=view.GetNode<GridContainer>("%Skills"); _skillTitle=view.GetNode<Label>("%SkillTitle"); _skillDescription=view.GetNode<Label>("%SkillDescription");
        _skillSlot=view.GetNode<OptionButton>("%SkillSlot"); _skillSlot.AddItem("Снять"); foreach(var key in Keys) _skillSlot.AddItem(key);
        _skillAction=view.GetNode<Button>("%SkillAction"); _skillAction.Pressed+=ActivateSkill;
        _cancel=view.GetNode<Button>("%Cancel"); _cancel.Pressed+=Cancel;
        _apply=view.GetNode<Button>("%Apply"); _apply.Pressed+=()=>Send(ProgressionAction.AllocateStats);
        view.GetNode<Button>("%Allocate").Pressed+=()=>_tabs.CurrentTab=1;
        view.GetNode<Button>("%Profession").Pressed+=()=>GameUi.Navigate("profession");
        _tabs.TabChanged+=_=>Refresh();
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
    private void EquipmentChanged(InventoryState value) { if (value.EntityId == _state.OwnerId || !_state.OwnerId.IsValid) { _display.ApplyEquipment(value); Invalidate(); } }
    private void ConditionChanged(ItemConditionState value) { if (value.OwnerId == _state.OwnerId) Invalidate(); }
    public void Result(ProgressionResult result)
    {
        if (result.Sequence != _pending) return;
        // Accepted preview follows its result on the same reliable ordered stream.
        if (_pendingAction == ProgressionAction.PreviewStats && result.Outcome == ProgressionOutcome.Accepted) return;
        var allocation = _pendingAction == ProgressionAction.AllocateStats; _pending = 0;
        if (result.Outcome == ProgressionOutcome.Accepted)
        { if (allocation) { Array.Clear(_draft); Invalidate(); } _feedback.Text = "Изменения сохранены"; }
        else
        {
            _feedback.Text = result.Outcome switch { ProgressionOutcome.NoPoints => "Недостаточно очков", ProgressionOutcome.Busy => "Дождитесь завершения действия", ProgressionOutcome.RateLimited => "Слишком часто. Попробуйте ещё раз", ProgressionOutcome.InvalidState => "Сейчас действие недоступно", ProgressionOutcome.StalePreview => "Характеристики изменились. Обновляем предпросмотр — подтвердите заново", _ => "Действие недоступно" };
            if (result.Outcome == ProgressionOutcome.StalePreview) Invalidate();
        }
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
    { if (PcInput.Pressed("character",ev)) { Toggle(); GetViewport().SetInputAsHandled(); } }
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
        var previewSequence = action == ProgressionAction.AllocateStats && _previewVersion == _version && _preview is { } preview
            ? preview.Sequence : 0;
        _network.SendProgression(new(_sequence,action,0,skill,slot,allocation,previewSequence)); Refresh(); RebuildSkills();
    }
    private void Refresh()
    {
        if (_points is null) return;
        var total = _draft.Sum(); var busy = _pending != 0 && _pendingAction != ProgressionAction.PreviewStats;
        _points.Text = $"Путешественник · Уровень {_state.Level}"+(_tabs.CurrentTab==1?$"    Доступно очков: {_state.StatPoints-total}"+(total>0?$" · Выбрано: {total}":""):"");
        _draftActions.Visible=total>0;
        for (var i=0;i<7;i++)
        {
            _stats[i].Text = $"{StatNames[i]}  {(_state.Stats is null ? 0 : _state.Stats[i]):0}"+(_draft[i]>0 ? $" → {_state.Stats![i]+_draft[i]:0}" : "");
            _plus[i].Disabled = busy || !_alive || total >= _state.StatPoints; _minus[i].Disabled = busy || !_alive || _draft[i] == 0;
            _summary[i].Text=(_state.Stats is null?0:_state.Stats[i]).ToString("0");
        }
        _apply.Disabled = !_alive || _pending != 0 || total == 0 || _previewVersion != _version; _cancel.Disabled = busy || total == 0;
        int[] summaryIndices=[7,8,5,6];
        for(var i=0;i<summaryIndices.Length;i++) _summary[7+i].Text=_preview is {} p?Format(summaryIndices[i],p.Current[summaryIndices[i]]):"—";
        for (var i=0;i<_derived.Length;i++)
        {
            var current = _preview is { } p ? Format(i,p.Current[i]) : "—";
            var after = _previewVersion == _version && _preview is { } v ? Format(i,v.Projected[i]) : "…";
            _derived[i].Text = $"{DerivedNames[i]}: {current}"+(total>0 ? $" → {after}" : "");
            _derived[i].Modulate = _previewVersion == _version && _preview is { } ready && ready.Projected[i] != ready.Current[i] ? GameUi.Accent : Colors.White;
        }
    }
    internal static string Format(int i, double value)
    {
        // Multipliers are displayed as bonuses relative to the unchanged 100% baseline.
        if (i is 3 or 4 or 11 or 12 or 13 or 14)
            return (value - 1).ToString("+0.#%;-0.#%;0%");
        return i is 15 or 16 or 19 or 21 ? $"{value*100:0.#}%" : $"{value:0}";
    }
    private void RebuildSkills()
    {
        if(_skills is null || _state.Skills is null) return;
        foreach(var child in _skills.GetChildren()) { _skills.RemoveChild(child); child.QueueFree(); }
        if(!_state.Skills.Any(s=>s.Id==_selectedSkill)) _selectedSkill=_state.Skills.FirstOrDefault().Id;
        var scene=GD.Load<PackedScene>("res://UI/Components/SkillCard.tscn");
        foreach(var skill in _state.Skills)
        {
            var card=scene.Instantiate<VBoxContainer>(); _skills.AddChild(card);
            var icon=card.GetNode<UiIconSlot>("Icon"); icon.ToggleMode=true; icon.ButtonPressed=skill.Id==_selectedSkill;
            icon.Bind(UiAssets.SkillKey(skill.Id),GameUi.SkillName(skill.Id),"",skill.Id is >=20 and <=30?"equipment.weapon":"icon.star");
            icon.Pressed+=()=> { _selectedSkill=skill.Id; RebuildSkills(); };
            card.GetNode<Label>("Title").Text=GameUi.SkillName(skill.Id);
            card.GetNode<Label>("State").Text=skill.Level==0?"Не изучен":skill.Id==3?"Space":skill.IsPassive?"Пассивный":(skill.Slot>0?Keys[skill.Slot-1]+" · ":"")+"Ур. "+skill.Level;
        }
        var selected=_state.Skills.FirstOrDefault(s=>s.Id==_selectedSkill);
        _skillTitle.Text=selected.Id==0?"Нет известных навыков":GameUi.SkillName(selected.Id);
        _skillDescription.Text=selected.Id==0?"Новые сведения появятся по мере приключений.":
            (selected.Id is >=20 and <=30?SwordsmanUi.Description(selected.Id)+"\n\n":"")+
            (selected.Level==0?"Навык ещё не изучен.":$"Уровень {selected.Level}\nОсвоение: {selected.Practice} / {selected.NextPractice}")+
            (selected.IsPassive?"\n\nПассивный навык. Ячейка не нужна.":selected.Id==3?"\n\nРывок назначен на Space.":"");
        _skillSlot.Visible=selected.Id!=0 && selected.Level>0 && !selected.IsPassive && selected.Id!=3;
        _skillSlot.Select(Math.Clamp((int)selected.Slot,0,8)); _skillSlot.Disabled=_pending!=0 || !_alive;
        _skillAction.Visible=selected.Id!=0 && !selected.IsPassive && selected.Id!=3;
        _skillAction.Text=selected.Level==0?"Изучить":"Назначить";
        _skillAction.Disabled=_pending!=0 || !_alive || selected.Level==0 && !selected.Learnable;
    }
    private void ActivateSkill()
    {
        var skill=_state.Skills?.FirstOrDefault(s=>s.Id==_selectedSkill)??default;
        if(skill.Id==0) return;
        Send(skill.Level==0?ProgressionAction.LearnSkill:ProgressionAction.AssignSlot,skill.Id,(byte)_skillSlot.Selected);
    }
    private static double Now() => Time.GetTicksMsec()/1000d;
}
