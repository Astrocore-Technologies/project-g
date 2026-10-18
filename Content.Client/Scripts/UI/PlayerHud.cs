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
    private Label _combatText=null!;
    private DefenseState _defense;
    private double _defenseReceived;
    private readonly Label[] _slotNames = new Label[9], _slotTimers = new Label[9];
    private readonly Label[] _slotKeys = new Label[9];
    private readonly PanelContainer[] _slots = new PanelContainer[9];
    private readonly TextureRect[] _slotArt = new TextureRect[9];
    private readonly ushort[] _shownArt = new ushort[9];
    private readonly ushort[] _bar = new ushort[8];
    private AbilityLoadout _loadout;
    private double _received, _refresh;
    private bool _alive = true;
    private double _health;
    private NetworkEntityId _owner;
    private Button _portrait = null!;
    private TextureRect _portraitArt = null!;
    private Action? _openCharacter;
    public void Initialize(NetworkEntityId owner, Action character, Texture2D? portrait = null)
    {
        _owner = owner;
        _openCharacter = character;
        Layer = 3;
        _root = GD.Load<PackedScene>("res://UI/Screens/PlayerHudView.tscn").Instantiate<Control>();
        AddChild(_root);
        var resources = _root.GetNode<Control>("PlayerResources");
        _portrait = resources.GetNode<Button>("%Portrait");
        _portraitArt = resources.GetNode<TextureRect>("%Art");
        SetPortrait(portrait);
        _portrait.Pressed += OpenCharacter;
        _level = resources.GetNode<Label>("%Level");
        _combatText = _root.GetNode<Label>("%CombatStatus");
        _hp = resources.GetNode<ProgressBar>("%Health");
        _hpText = resources.GetNode<Label>("%HealthText");
        _mana = resources.GetNode<ProgressBar>("%Mana");
        _manaText = resources.GetNode<Label>("%ManaText");
        _stamina = resources.GetNode<ProgressBar>("%Stamina");
        _staminaText = resources.GetNode<Label>("%StaminaText");
        _xp = _root.GetNode<ProgressBar>("%Experience");
        _xpText = _root.GetNode<Label>("%ExperienceText");
        _defenseText = _root.GetNode<Label>("%DefenseText");
        InitializeControls(_root);
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _root.GetNode<PanelContainer>($"Hotbar/Skill{i}");
            _slots[i] = slot;
            _slotArt[i] = slot.GetNode<TextureRect>("Content/Icon");
            _slotNames[i] = slot.GetNode<Label>("Content/SkillName");
            _slotTimers[i] = slot.GetNode<Label>("Content/Timer");
            _slotKeys[i] = slot.GetNode<Label>("Content/Key");
        }
        PcInput.Changed += RefreshBindings; RefreshBindings();
    }
    private void RefreshBindings(){for(var i=0;i<_slotKeys.Length;i++)_slotKeys[i].Text=PcInput.Label(i==8?PcInput.Dodge:PcInput.Skills[i]);}

    public void SetPortrait(Texture2D? portrait) => _portraitArt.Texture = portrait;
    private void OpenCharacter() => _openCharacter?.Invoke();
    public override void _ExitTree()
    {
        PcInput.Changed -= RefreshBindings;
        if (_portrait is not null) _portrait.Pressed -= OpenCharacter;
        _openCharacter = null;
    }
    private static void Value(ProgressBar bar, double value, double max) { bar.MaxValue = Math.Max(1,max); bar.Value = Math.Clamp(value,0,bar.MaxValue); }
    public void Health(double health, double max)
    { _health=health;_alive = health>0; _hpText.Text = $"{health:0} / {max:0}"; _hp.TooltipText="Здоровье: "+_hpText.Text+(_alive ? "" : " · Вы повержены"); Value(_hp,health,max); }
    public void Apply(DefenseState state)
    {
        if (state.OwnerId!=_owner) return;
        _defense=state; _defenseReceived=Time.GetTicksMsec()/1000d;
        _staminaText.Text=$"{state.Stamina:0} / {state.MaxStamina:0}";
        _stamina.TooltipText="Выносливость: "+_staminaText.Text;
        Value(_stamina,state.Stamina,state.MaxStamina);
        _defenseText.TooltipText=$"Блок урона: {state.BlockDamage*100:0.#}%\nПарирование: {state.ParryCost:0} выносливости\nРывок: {state.DodgeCost:0} выносливости\n{PcInput.Label(PcInput.Dodge)} при падении: Quick Recover, {state.QuickRecoverCost:0} выносливости, до {state.QuickRecoverRange:0.#} м. Удерживать — прицел, отпустить — перекат.";
    }
    public void Apply(AbilityLoadout loadout)
    { if (loadout.EntityId != _owner) return; _loadout = loadout; _received = Time.GetTicksMsec()/1000d; _manaText.Text = $"{loadout.Mana:0} / {loadout.MaxMana:0}"; _mana.TooltipText="Мана: "+_manaText.Text; Value(_mana,loadout.Mana,loadout.MaxMana); UpdateSlots(); }
    public void Apply(ProgressionState state)
    {
        _level.Text = $"Путешественник · {state.Level}"; _level.TooltipText = $"Свободных очков: {state.StatPoints} · K — распределить";
        _portrait.TooltipText=_level.Text+"\n"+_level.TooltipText;
        _xpText.Text = state.NextExperience == 0 ? "Максимальный уровень" : $"Опыт  {state.Experience} / {state.NextExperience}   ·   Очки: {state.StatPoints}";
        _xp.TooltipText=_xpText.Text;
        Value(_xp,state.NextExperience == 0 ? 1 : state.Experience,state.NextExperience == 0 ? 1 : state.NextExperience);
        Array.Clear(_bar); foreach (var skill in state.Skills) if (skill.Slot>0) _bar[skill.Slot-1] = skill.Id; UpdateSlots();
    }
    public override void _Process(double delta) { _root.Visible=!GameUi.GameplayModalOpen; _refresh+=delta; if (_refresh<.1) return; _refresh=0; UpdateSlots(); UpdateControls(); }
    private void UpdateSlots()
    {
        var inCombat=Math.Max(0,_defense.InCombatRemaining-(Time.GetTicksMsec()/1000d-_defenseReceived));
        _combatText.Visible=inCombat>0;
        _combatText.Text=inCombat>0 ? $"В БОЮ · {inCombat:0.0} с" : "";
        var parry=Math.Max(0,_defense.ParryCooldown-(Time.GetTicksMsec()/1000d-_defenseReceived));
        _defenseText.Text=(_defense.Blocking ? PcInput.Label(PcInput.Block)+" · БЛОК" : PcInput.Label(PcInput.Block)+" · блок")+(parry>0 ? $"   {PcInput.Label(PcInput.Parry)} · {parry:0.0} с" : "   "+PcInput.Label(PcInput.Parry)+" · парирование");
        var guardBreak=Math.Max(0,_defense.GuardBreakRemaining-(Time.GetTicksMsec()/1000d-_defenseReceived));
        if (guardBreak>0) _defenseText.Text=$"Блок пробит · {guardBreak:0.0} с"+(parry>0 ? $"   {PcInput.Label(PcInput.Parry)} · {parry:0.0} с" : "   "+PcInput.Label(PcInput.Parry)+" · парирование");
        var quick=Math.Max(0,_defense.QuickRecoverCooldown-(Time.GetTicksMsec()/1000d-_defenseReceived));
        _defenseText.Visible=inCombat>0 || _defense.Blocking || parry>0 || guardBreak>0 || quick>0;
        if(quick>0) _defenseText.Text+=$"   Подъём · {quick:0.0} с";
        if (_loadout.Abilities is null) return;
        var elapsed = Time.GetTicksMsec()/1000d-_received;
        for (var i=0;i<9;i++)
        {
            AbilityProfile? found = null;
            foreach (var profile in _loadout.Abilities) if (i==8 ? profile.Form==AbilityForm.Dash : profile.Id==_bar[i]) { found=profile; break; }
            if (found is not { } skill) { _slotArt[i].Texture=null; _shownArt[i]=0; _slotNames[i].Text=""; _slotTimers[i].Text=""; _slots[i].TooltipText="Назначьте навык в окне K"; continue; }
            if (_shownArt[i]!=skill.Id) { _shownArt[i]=skill.Id; _slotArt[i].Texture=UiAssets.Texture(UiAssets.SkillKey(skill.Id),skill.Id is >=20 and <=30 ? "equipment.weapon" : null); }
            var cooldown = Math.Max(0,skill.ReadyInSeconds-elapsed);
            var exhausted=_defense.Stamina<skill.StaminaCost;
            _slotNames[i].Text=GameUi.SkillName(skill.Id); _slotTimers[i].Text=!_alive ? "—" : cooldown>0 ? $"{cooldown:0.0} с" : _loadout.Mana<skill.ManaCost ? "Нет маны" : "Готово";
            _slotTimers[i].Modulate=cooldown<=0 && _loadout.Mana>=skill.ManaCost && _alive ? GameUi.Accent : new Color("a9aeb7");
            _slots[i].TooltipText=$"{GameUi.SkillName(skill.Id)}\nМана: {skill.ManaCost:0}\nКаст: {skill.CastSeconds:0.##} с";
            if (skill.Form==AbilityForm.Dash)
            {
                _slotNames[i].Text="Уворот";
                _slots[i].TooltipText=$"Уворот\nВыносливость: {skill.StaminaCost:0}\nДо {skill.Range:0.#} м за {skill.DodgeDurationSeconds:0.##} с · перезарядка {skill.CooldownSeconds:0.#} с\nЗащита: первые {skill.DodgeProtectionSeconds:0.##} с — {DodgeThreatLabels(skill.DodgeProtectedThreats)}\n{PcInput.Label(PcInput.Dodge)} · до курсора. Контроль проходит по обычным правилам.";
            }
            if (skill.Id is >=20 and <=30) _slots[i].TooltipText=$"{GameUi.SkillName(skill.Id)}\n{SwordsmanUi.Description(skill.Id)}\nТекущий расход: {skill.StaminaCost:0}; перезарядка: {skill.CooldownSeconds:0.#} с";
            if (skill.RecoverySeconds>0) _slots[i].TooltipText+=$"\nВосстановление после удара: до {skill.RecoverySeconds:0.##} с";
            if(skill.HealthCost>0)_slots[i].TooltipText+=$"\nСтоимость HP: {skill.HealthCost:0.##}"+
                (skill.AllowFatalHealthCost?" · может привести к смерти":" · требуется остаток HP выше нуля");
            if(skill.HealthCost>0 && (_health<skill.HealthCost || !skill.AllowFatalHealthCost&&_health<=skill.HealthCost))
            {_slotTimers[i].Text="Нет HP";_slotTimers[i].Modulate=new Color("ee9b8a");}
            if(skill.MaxCharges>1)
            {
                _slotTimers[i].Text=$"{skill.Charges}/{skill.MaxCharges} · "+_slotTimers[i].Text;
                _slots[i].TooltipText+=$"\nЗаряды: {skill.Charges}/{skill.MaxCharges}";
                if(skill.Charges+skill.PendingEndCharges<skill.MaxCharges) _slots[i].TooltipText+=$"\nСледующий заряд: {Math.Max(0,skill.RechargeInSeconds-elapsed):0.0} с";
            }
            if(skill.ExecutionForm is Content.Shared.Combat.CombatActionForm.Channel or Content.Shared.Combat.CombatActionForm.Hold or Content.Shared.Combat.CombatActionForm.Toggle)
            {
                _slots[i].TooltipText+=$"\nПоддержание: {skill.ChannelManaPerSecond:0.##} MP/с, {skill.ChannelStaminaPerSecond:0.##} выносливости/с";
                _slots[i].TooltipText+=skill.ChannelMaximumSeconds>0?$"\nДо {skill.ChannelMaximumSeconds:0.##} с":"\nДо прекращения поддержания";
                _slots[i].TooltipText+="\nEsc — прекратить поддержание";
                if(skill.PendingEndCharges>0 && skill.Charges==0){_slotTimers[i].Text="Поддерживается";_slotTimers[i].Modulate=new Color("a9aeb7");}
            }
            if(skill.ExecutionForm==Content.Shared.Combat.CombatActionForm.Charged)
            {
                _slots[i].TooltipText+=$"\nПолная зарядка: {skill.ChargeSeconds:0.##} с · "+(skill.ChargeAutoRelease?"автовыпуск":"удержание до отпускания");
                _slots[i].TooltipText+="\nНажатие — зарядка; отпускание — выпуск; Esc/ПКМ — отмена";
            }
            if(skill.ExecutionForm==Content.Shared.Combat.CombatActionForm.Hold)
                _slots[i].TooltipText+="\nНажатие — начало; удержание — работа; отпускание — завершение";
            if(skill.ExecutionForm==Content.Shared.Combat.CombatActionForm.Toggle)
            {
                _slots[i].TooltipText+="\nНажатие — включить; повторное нажатие — выключить";
                _slots[i].TooltipText+=skill.ToggleOccupiesAction?"\nЗанимает обычное действие":"\nДействует вместе с обычным действием";
                if(skill.ToggleStopOn!=Content.Shared.Combat.ToggleStopTriggers.None)_slots[i].TooltipText+=$"\nДополнительные условия прекращения: {ToggleStops(skill.ToggleStopOn)}";
                if(skill.ToggleState!=Content.Shared.Combat.ToggleState.Off)
                {_slotTimers[i].Text=skill.ToggleState==Content.Shared.Combat.ToggleState.Preparing?"Включается":"Включён";_slotTimers[i].Modulate=GameUi.Accent;}
            }
            if(skill.ExecutionForm is not (Content.Shared.Combat.CombatActionForm.Charged or Content.Shared.Combat.CombatActionForm.Hold or Content.Shared.Combat.CombatActionForm.Toggle))
                _slots[i].TooltipText += "\nУдерживайте клавишу — область применения; отпустите — применить.\nEsc или ПКМ — отменить прицеливание.";
            if (skill.Availability!=AbilityAvailability.Ready && _alive && cooldown<=0 && skill.ToggleState==Content.Shared.Combat.ToggleState.Off)
            { _slotTimers[i].Text=skill.Availability switch {AbilityAvailability.NeedsSword=>"Нужен меч",AbilityAvailability.NeedsWeapon=>"Нужно оружие",_=>"Парируй"}; _slotTimers[i].Modulate=new Color("a9aeb7"); }
            if (exhausted && _alive && cooldown<=0 && skill.ToggleState==Content.Shared.Combat.ToggleState.Off) { _slotTimers[i].Text="Нет сил"; _slotTimers[i].Modulate=new Color("a9aeb7"); }
            if(_slotTimers[i].Text=="Готово") _slotTimers[i].Text="";
        }
    }

    private static string ToggleStops(Content.Shared.Combat.ToggleStopTriggers flags)
    {
        if(ToggleStopLabels[(byte)flags] is {} cached)return cached;
        var labels=new List<string>();
        if((flags&Content.Shared.Combat.ToggleStopTriggers.Movement)!=0)labels.Add("движение");
        if((flags&Content.Shared.Combat.ToggleStopTriggers.Skill)!=0)labels.Add("другой навык");
        if((flags&Content.Shared.Combat.ToggleStopTriggers.BasicAttack)!=0)labels.Add("обычная атака");
        if((flags&Content.Shared.Combat.ToggleStopTriggers.Defense)!=0)labels.Add("защита");
        if((flags&Content.Shared.Combat.ToggleStopTriggers.EnemyInterrupt)!=0)labels.Add("прерывание противником");
        return ToggleStopLabels[(byte)flags]=string.Join(", ",labels);
    }
    private static readonly string?[] ToggleStopLabels=new string?[32];

    private static string DodgeThreatLabels(Content.Shared.Combat.DodgeThreats flags)
    {
        if((byte)flags>=DodgeThreatCache.Length)return "неизвестный вид урона";
        if(DodgeThreatCache[(byte)flags] is {} cached)return cached;
        var labels=new List<string>();
        if((flags&Content.Shared.Combat.DodgeThreats.DirectPhysical)!=0)labels.Add("прямой физический урон");
        if((flags&Content.Shared.Combat.DodgeThreats.DirectMagical)!=0)labels.Add("прямой магический урон");
        if((flags&Content.Shared.Combat.DodgeThreats.PeriodicPhysical)!=0)labels.Add("периодический физический урон");
        if((flags&Content.Shared.Combat.DodgeThreats.PeriodicMagical)!=0)labels.Add("периодический магический урон");
        return DodgeThreatCache[(byte)flags]=labels.Count==0?"нет защиты от урона":string.Join(", ",labels);
    }
    private static readonly string?[] DodgeThreatCache=new string?[16];
}
