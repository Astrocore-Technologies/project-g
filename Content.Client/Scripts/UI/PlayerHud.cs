using Content.Shared.Network;
using Godot;
namespace ProjectG.UI;

/// <summary>Owner resource HUD. Timers interpolate presentation only; values are server-confirmed.</summary>
public partial class PlayerHud : CanvasLayer
{
    private Label _level = null!, _hpText = null!, _manaText = null!, _xpText = null!;
    private ProgressBar _hp = null!, _mana = null!, _xp = null!;
    private ProgressBar _stamina = null!;
    private Label _staminaText = null!, _defenseText = null!;
    private DefenseState _defense;
    private double _defenseReceived;
    private readonly Label[] _slotNames = new Label[9], _slotTimers = new Label[9];
    private readonly PanelContainer[] _slots = new PanelContainer[9];
    private readonly ushort[] _bar = new ushort[8];
    private AbilityLoadout _loadout;
    private double _received, _refresh;
    private bool _alive = true;
    private NetworkEntityId _owner;
    public void Initialize(NetworkEntityId owner, Action character)
    {
        _owner = owner; Layer = 3;
        var root = new Control { Theme = GameUi.CreateTheme(), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var portraitPanel = new PanelContainer { Position = new(16,16), CustomMinimumSize = new(320,0) }; root.AddChild(portraitPanel);
        var body = new VBoxContainer(); portraitPanel.AddChild(body);
        var header = new HBoxContainer(); body.AddChild(header);
        var portrait = GameUi.Button("◇",character); portrait.FocusMode=Control.FocusModeEnum.None; portrait.CustomMinimumSize = new(46,46); portrait.TooltipText = "Персонаж · K"; header.AddChild(portrait);
        _level = GameUi.Text("Путешественник",18); _level.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; header.AddChild(_level);
        var open = GameUi.Button("K",character); open.FocusMode=Control.FocusModeEnum.None; header.AddChild(open);
        (_hp,_hpText) = Resource(body,"HP",new("bb6260"),22);
        (_mana,_manaText) = Resource(body,"Мана",new("548dc0"),22);
        (_stamina,_staminaText) = Resource(body,"Выносливость",new("80ac70"),16);
        (_xp,_xpText) = Resource(body,"Опыт",new("b8a168"),6); _xpText.AddThemeFontSizeOverride("font_size",12);
        _defenseText=GameUi.Text("Tab · блок   Shift · парирование",12); body.AddChild(_defenseText);
        var bar = new HBoxContainer { AnchorLeft=.5f, AnchorRight=.5f, AnchorTop=1, AnchorBottom=1, OffsetLeft=-366, OffsetRight=366, OffsetTop=-94, OffsetBottom=-16 };
        root.AddChild(bar);
        string[] keys = ["Q","W","E","R","A","S","D","F","Space"];
        for (var i=0;i<9;i++)
        {
            var panel = new PanelContainer { CustomMinimumSize = new(72,78), MouseFilter = Control.MouseFilterEnum.Stop };
            if (i == 8) { var spacer = new Control { CustomMinimumSize = new(8,0) }; bar.AddChild(spacer); }
            bar.AddChild(panel); _slots[i] = panel;
            var column = new VBoxContainer(); panel.AddChild(column); column.AddThemeConstantOverride("separation",2);
            var key = GameUi.Text(keys[i],14); key.Modulate = GameUi.Accent; column.AddChild(key);
            _slotNames[i] = GameUi.Text("—",12); _slotNames[i].CustomMinimumSize = new(48,0); _slotNames[i].TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; column.AddChild(_slotNames[i]);
            _slotTimers[i] = GameUi.Text("",12); column.AddChild(_slotTimers[i]);
        }
    }
    private static (ProgressBar,Label) Resource(VBoxContainer body, string name, Color color, float height)
    {
        var text = GameUi.Text(name,14); body.AddChild(text);
        var bar = new ProgressBar { CustomMinimumSize = new(0,height), ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("background",GameUi.Box(new("19242b"),0)); bar.AddThemeStyleboxOverride("fill",GameUi.Box(color,0)); body.AddChild(bar); return (bar,text);
    }
    private static void Value(ProgressBar bar, double value, double max) { bar.MaxValue = Math.Max(1,max); bar.Value = Math.Clamp(value,0,bar.MaxValue); }
    public void Health(double health, double max)
    { _alive = health>0; _hpText.Text = $"HP  {health:0} / {max:0}"+(_alive ? "" : " · Вы повержены"); Value(_hp,health,max); }
    public void Apply(DefenseState state)
    {
        if (state.OwnerId!=_owner) return;
        _defense=state; _defenseReceived=Time.GetTicksMsec()/1000d;
        _staminaText.Text=$"Выносливость  {state.Stamina:0} / {state.MaxStamina:0}";
        Value(_stamina,state.Stamina,state.MaxStamina);
        _defenseText.TooltipText=$"Блок урона: {state.BlockDamage*100:0.#}%\nПарирование: {state.ParryCost:0} выносливости\nРывок: {state.DodgeCost:0} выносливости";
    }
    public void Apply(AbilityLoadout loadout)
    { if (loadout.EntityId != _owner) return; _loadout = loadout; _received = Time.GetTicksMsec()/1000d; _manaText.Text = $"Мана  {loadout.Mana:0} / {loadout.MaxMana:0}"; Value(_mana,loadout.Mana,loadout.MaxMana); UpdateSlots(); }
    public void Apply(ProgressionState state)
    {
        _level.Text = $"Путешественник · {state.Level}"; _level.TooltipText = $"Свободных очков: {state.StatPoints} · K — распределить";
        _xpText.Text = state.NextExperience == 0 ? "Максимальный уровень" : $"Опыт  {state.Experience} / {state.NextExperience}   ·   Очки: {state.StatPoints}";
        Value(_xp,state.NextExperience == 0 ? 1 : state.Experience,state.NextExperience == 0 ? 1 : state.NextExperience);
        Array.Clear(_bar); foreach (var skill in state.Skills) if (skill.Slot>0) _bar[skill.Slot-1] = skill.Id; UpdateSlots();
    }
    public override void _Process(double delta) { _refresh+=delta; if (_refresh<.1) return; _refresh=0; UpdateSlots(); }
    private void UpdateSlots()
    {
        var parry=Math.Max(0,_defense.ParryCooldown-(Time.GetTicksMsec()/1000d-_defenseReceived));
        _defenseText.Text=(_defense.Blocking ? "Tab · БЛОК" : "Tab · блок")+(parry>0 ? $"   Shift · {parry:0.0} с" : "   Shift · парирование");
        if (_loadout.Abilities is null) return;
        var elapsed = Time.GetTicksMsec()/1000d-_received;
        for (var i=0;i<9;i++)
        {
            AbilityProfile? found = null;
            foreach (var profile in _loadout.Abilities) if (i==8 ? profile.Form==AbilityForm.Dash : profile.Id==_bar[i]) { found=profile; break; }
            if (found is not { } skill) { _slotNames[i].Text="—"; _slotTimers[i].Text="Пусто"; _slots[i].TooltipText="Назначьте навык в окне K"; continue; }
            var cooldown = Math.Max(0,skill.ReadyInSeconds-elapsed);
            var exhausted=skill.Form==AbilityForm.Dash && _defense.Stamina<_defense.DodgeCost;
            _slotNames[i].Text=GameUi.SkillName(skill.Id); _slotTimers[i].Text=!_alive ? "—" : cooldown>0 ? $"{cooldown:0.0} с" : _loadout.Mana<skill.ManaCost ? "Нет маны" : "Готово";
            _slotTimers[i].Modulate=cooldown<=0 && _loadout.Mana>=skill.ManaCost && _alive ? GameUi.Accent : new Color("a9aeb7");
            _slots[i].TooltipText=$"{GameUi.SkillName(skill.Id)}\nМана: {skill.ManaCost:0}\nКаст: {skill.CastSeconds:0.##} с\nИспользуйте клавишу, направляя курсор в мир.";
            if (skill.Form==AbilityForm.Dash) _slots[i].TooltipText=$"Рывок\nВыносливость: {_defense.DodgeCost:0}\nSpace · направление курсором";
            if (exhausted && _alive && cooldown<=0) { _slotTimers[i].Text="Нет сил"; _slotTimers[i].Modulate=new Color("a9aeb7"); }
        }
    }
}
