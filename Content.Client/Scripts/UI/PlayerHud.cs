using Content.Shared.Network;
using Godot;
namespace ProjectG.UI;

/// <summary>Owner resource HUD. Timers interpolate presentation only; values are server-confirmed.</summary>
public partial class PlayerHud : CanvasLayer
{
    private Control _root=null!;
    private Label _level = null!, _hpText = null!, _manaText = null!, _xpText = null!;
    private ProgressBar _hp = null!, _mana = null!, _xp = null!;
    private ProgressBar _stamina = null!;
    private Label _staminaText = null!, _defenseText = null!;
    private DefenseState _defense;
    private double _defenseReceived;
    private readonly Label[] _slotNames = new Label[9], _slotTimers = new Label[9];
    private readonly PanelContainer[] _slots = new PanelContainer[9];
    private readonly TextureRect[] _slotArt = new TextureRect[9];
    private readonly ushort[] _shownArt = new ushort[9];
    private readonly ushort[] _bar = new ushort[8];
    private AbilityLoadout _loadout;
    private double _received, _refresh;
    private bool _alive = true;
    private NetworkEntityId _owner;
    public void Initialize(NetworkEntityId owner, Action character)
    {
        _owner = owner; Layer = 3;
        var root = _root = new Control { Theme = GameUi.CreateTheme(), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var portraitPanel = new PanelContainer { AnchorLeft=.5f,AnchorRight=.5f,AnchorTop=1,AnchorBottom=1,OffsetLeft=-328,OffsetRight=328,OffsetTop=-290,OffsetBottom=-124 }; root.AddChild(portraitPanel);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation",3); portraitPanel.AddChild(body);
        var header = new HBoxContainer(); body.AddChild(header);
        var portrait = GameUi.Button("◇",character); portrait.FocusMode=Control.FocusModeEnum.None; portrait.CustomMinimumSize = new(36,32); portrait.TooltipText = "Персонаж · K"; header.AddChild(portrait);
        if(UiAssets.Texture("nav.character") is {} portraitIcon) { portrait.Text=""; portrait.Icon=portraitIcon; portrait.ExpandIcon=true; portrait.AddThemeConstantOverride("icon_max_width",24); }
        _level = GameUi.Text("Путешественник",18); _level.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; header.AddChild(_level);
        var open = GameUi.Button("K",character); open.FocusMode=Control.FocusModeEnum.None; header.AddChild(open);
        (_hp,_hpText) = Resource(body,"HP",UiAssets.Skin.Health,22,UiAssets.Skin.HealthFill);
        (_mana,_manaText) = Resource(body,"Мана",UiAssets.Skin.Mana,22,UiAssets.Skin.ManaFill);
        (_stamina,_staminaText) = Resource(body,"Выносливость",UiAssets.Skin.Stamina,16,UiAssets.Skin.StaminaFill);
        (_xp,_xpText) = Resource(body,"Опыт",new("b8a168"),6,UiAssets.Skin.ExperienceFill); _xpText.AddThemeFontSizeOverride("font_size",12);
        _defenseText=GameUi.Text("Tab · блок   Shift · парирование",12); body.AddChild(_defenseText);
        var bar = new HBoxContainer { AnchorLeft=.5f, AnchorRight=.5f, AnchorTop=1, AnchorBottom=1, OffsetLeft=-350, OffsetRight=350, OffsetTop=-118, OffsetBottom=-16 };
        root.AddChild(bar);
        string[] keys = ["Q","W","E","R","A","S","D","F","Space"];
        for (var i=0;i<9;i++)
        {
            var panel = new PanelContainer { CustomMinimumSize = new(68,100), MouseFilter = Control.MouseFilterEnum.Stop };
            if (i == 8) { var spacer = new Control { CustomMinimumSize = new(8,0) }; bar.AddChild(spacer); }
            bar.AddChild(panel); _slots[i] = panel;
            var column = new VBoxContainer(); panel.AddChild(column); column.AddThemeConstantOverride("separation",2);
            _slotArt[i] = new TextureRect { CustomMinimumSize=new(26,26), SizeFlagsHorizontal=Control.SizeFlags.ShrinkCenter, ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize, StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter=Control.MouseFilterEnum.Ignore }; column.AddChild(_slotArt[i]);
            var key = GameUi.Text(keys[i],14); key.Modulate = GameUi.Accent; column.AddChild(key);
            _slotNames[i] = GameUi.Text("—",12); _slotNames[i].CustomMinimumSize = new(48,0); _slotNames[i].TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; column.AddChild(_slotNames[i]);
            _slotTimers[i] = GameUi.Text("",12); column.AddChild(_slotTimers[i]);
        }
        GameUi.CompactHud(portraitPanel); GameUi.CompactHud(bar);
    }
    private static (ProgressBar,Label) Resource(VBoxContainer body,string name,Color color,float height,StyleBox? fill=null)
    {
        var row=new Control { CustomMinimumSize=new(0,Math.Max(18,height)) }; body.AddChild(row);
        var bar=new ProgressBar { ShowPercentage=false,MouseFilter=Control.MouseFilterEnum.Ignore }; row.AddChild(bar); bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bar.AddThemeStyleboxOverride("background",UiAssets.Skin.GaugeBackground??GameUi.Box(new("19242b"),0)); bar.AddThemeStyleboxOverride("fill",fill??GameUi.Box(color,0));
        var text=GameUi.Text(name,12); text.HorizontalAlignment=HorizontalAlignment.Center; text.VerticalAlignment=VerticalAlignment.Center;
        row.AddChild(text); text.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); return(bar,text);
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
    public override void _Process(double delta) { _root.Visible=!GameUi.GameplayModalOpen; _refresh+=delta; if (_refresh<.1) return; _refresh=0; UpdateSlots(); }
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
            if (found is not { } skill) { _slotArt[i].Texture=null; _shownArt[i]=0; _slotNames[i].Text="—"; _slotTimers[i].Text="Пусто"; _slots[i].TooltipText="Назначьте навык в окне K"; continue; }
            if (_shownArt[i]!=skill.Id) { _shownArt[i]=skill.Id; _slotArt[i].Texture=UiAssets.Texture(UiAssets.SkillKey(skill.Id),skill.Id is >=20 and <=29 ? "equipment.weapon" : null); }
            var cooldown = Math.Max(0,skill.ReadyInSeconds-elapsed);
            var exhausted=_defense.Stamina<skill.StaminaCost;
            _slotNames[i].Text=GameUi.SkillName(skill.Id); _slotTimers[i].Text=!_alive ? "—" : cooldown>0 ? $"{cooldown:0.0} с" : _loadout.Mana<skill.ManaCost ? "Нет маны" : "Готово";
            _slotTimers[i].Modulate=cooldown<=0 && _loadout.Mana>=skill.ManaCost && _alive ? GameUi.Accent : new Color("a9aeb7");
            _slots[i].TooltipText=$"{GameUi.SkillName(skill.Id)}\nМана: {skill.ManaCost:0}\nКаст: {skill.CastSeconds:0.##} с\nИспользуйте клавишу, направляя курсор в мир.";
            if (skill.Form==AbilityForm.Dash) { _slotNames[i].Text=skill.Range>3 ? "Длинный рывок" : "Рывок"; _slots[i].TooltipText=$"{_slotNames[i].Text}\nВыносливость: {skill.StaminaCost:0}\nДо {skill.Range:0.#} м · {skill.CooldownSeconds:0.#} с\nSpace · до курсора, без неуязвимости"; }
            if (skill.Id is >=20 and <=29) _slots[i].TooltipText=$"{GameUi.SkillName(skill.Id)}\n{SwordsmanUi.Description(skill.Id)}\nТекущий расход: {skill.StaminaCost:0}; перезарядка: {skill.CooldownSeconds:0.#} с";
            if (skill.Availability!=AbilityAvailability.Ready && _alive && cooldown<=0)
            { _slotTimers[i].Text=skill.Availability==AbilityAvailability.NeedsSword ? "Нужен меч" : "Парируй"; _slotTimers[i].Modulate=new Color("a9aeb7"); }
            if (exhausted && _alive && cooldown<=0) { _slotTimers[i].Text="Нет сил"; _slotTimers[i].Modulate=new Color("a9aeb7"); }
        }
    }
}
