using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;

namespace ProjectG.Echoes;

/// <summary>Three owner-only commands; local timers are presentation, not authority.</summary>
public partial class EchoControls : CanvasLayer
{
    private NetworkClient _network = null!;
    private NetworkEntityId _owner;
    private UiWindow _window=null!;
    private UiModelPreview _preview=null!;
    private VBoxContainer _list=null!;
    private Label _name=null!,_details=null!,_readiness=null!;
    private Control _hud=null!;
    private readonly Dictionary<byte,(EchoSpawn State,Mesh Mesh,Material? Material)> _known=new();
    private EchoLoadout _loadout;
    private byte _selected=1;
    private double _sentAt;

    private readonly Button[] _buttons = new Button[NetworkConstants.MaxActiveEchoes];
    private readonly double[] _cooldowns = new double[NetworkConstants.MaxActiveEchoes];
    private readonly bool[] _refused = new bool[NetworkConstants.MaxActiveEchoes];
    private Label _status = null!;
    private bool _alive = true, _pending;
    private uint _sequence;
    private static readonly string[] Keys = ["T", "Y", "U"];
    public void Initialize(NetworkClient network,NetworkEntityId owner)
    {
        _network=network; _owner=owner; Layer=22;
        var root=new Control { Theme=GameUi.CreateTheme(),MouseFilter=Control.MouseFilterEnum.Ignore }; AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel=new VBoxContainer { Name="CompanionHud",Visible=false,AnchorLeft=1,AnchorRight=1,OffsetLeft=-172,OffsetRight=-16,OffsetTop=136 }; root.AddChild(panel); _hud=panel;
        for(var i=0;i<_buttons.Length;i++)
        {
            var slot=(byte)(i+1); var button=new Button { Visible=false,CustomMinimumSize=new(144,62),FocusMode=Control.FocusModeEnum.None,Icon=UiAssets.Texture("icon.star"),ExpandIcon=true };
            button.AddThemeConstantOverride("icon_max_width",14); button.Pressed+=()=> { _selected=slot; Toggle(); }; panel.AddChild(button); _buttons[i]=button;
        }
        _status=UiComposition.Paragraph(panel,"T / Y / U — способности\nF3 — Эхо Прошлого",12); _status.AddThemeConstantOverride("outline_size",4); _status.AddThemeColorOverride("font_outline_color",new Color(0,0,0,.8f));
        _window=new UiWindow { ToggleKey=Key.F3 }; root.AddChild(_window); _window.Build("Эхо Прошлого",new(1180,656)); _window.CloseRequested+=_window.Close;
        var page=UiComposition.Page(_window,"echo");
        var columns=new HBoxContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; page.AddChild(columns);
        _list=new VBoxContainer { CustomMinimumSize=new(130,0) }; columns.AddChild(_list);
        var model=UiComposition.Card(columns,280); _preview=new UiModelPreview { CustomMinimumSize=new(240,280),SizeFlagsVertical=Control.SizeFlags.ExpandFill }; model.AddChild(_preview);
        UiComposition.Paragraph(model,"ЛКМ — поворот · колесо — масштаб",12);
        var detail=UiComposition.Card(columns,310); _name=UiComposition.Paragraph(detail,"Спутники",26);
        _readiness=UiComposition.Paragraph(detail,"",14);
        _details=UiComposition.Paragraph(detail,"",17); _details.SizeFlagsVertical=Control.SizeFlags.ExpandFill;
        var tabs=new HBoxContainer(); detail.AddChild(tabs);
        tabs.AddChild(GameUi.Button("Обзор",Render));
        tabs.AddChild(GameUi.Button("Память",()=>_details.Text="Воспоминания этого Эхо пока не открыты."));
        tabs.AddChild(GameUi.Button("Связь",()=>_details.Text="Сведения об отношениях с этим Эхо пока недоступны."));
        detail.AddChild(GameUi.Button("Вернуться к приключению",_window.Close));
        _network.EchoRosterReceived+=ApplyRoster;_network.EchoSummonReceived+=ApplySummon;_network.EchoRecallReceived+=ApplyRecall;
        _network.EchoRestoreReceived+=ApplyRestore;
        InitializeSignatureInput();
        if(_network.LatestEchoRoster is {} roster)ApplyRoster(roster);else Render();
    }
    public void Toggle() { if(_window.Visible) _window.Close(); else { RequestRoster(_roster?.Page??0);Render(); _window.Open(); } }
    public void Observe(EchoSpawn state,Mesh mesh,Material? material,EchoPresentation? body=null)
    { if(state.OwnerId!=_owner) return; _known[state.Slot]=(state,mesh,material);if(body is not null)_signatureBodies[state.EntityId]=body; Render(); }
    public void Remove(NetworkEntityId id)
    { foreach(var key in _known.Where(p=>p.Value.State.EntityId==id).Select(p=>p.Key).ToArray()) _known.Remove(key);ForgetSignature(id); Render(); }
    private void Render()
    {
        foreach(var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
        if(RenderRoster())return;
        foreach(var entry in _known.OrderBy(p=>p.Key)) { var slot=entry.Key; _list.AddChild(GameUi.Button(entry.Value.State.Name,()=> { _selected=slot; Render(); })); }
        if(!_known.TryGetValue(_selected,out var echo)) { _preview.Visible=false; _name.Text="Эхо Прошлого"; _details.Text="У вас пока нет Эхо. Сначала нужно получить спутника."; return; }
        _preview.Visible=true; _preview.SetMesh(echo.Mesh,echo.Material);
        _name.Text=echo.State.Name;
        var ability=_loadout.Slots?.FirstOrDefault(s=>s.Slot==_selected);
        _details.Text=$"Активный спутник · место {_selected}\n\nОсобая способность · {Keys[_selected-1]}\n"+(ability is {} a && a.Slot!=0?$"Дальность: {a.Range:0.#} м\nРадиус: {a.Radius:0.#} м":"Сведения о способности загружаются…")+"\n\nВ бою направьте курсор в мир и нажмите клавишу спутника.";
    }
    public void Apply(EchoLoadout value)
    {
        if(value.OwnerId!=_owner) return; _loadout=value;
        PruneSignatures();
        foreach (var button in _buttons) button.Visible = false;
        Array.Clear(_refused);
        foreach (var slot in value.Slots) { _buttons[slot.Slot - 1].Visible = true; _cooldowns[slot.Slot - 1] = slot.CooldownSeconds;_refused[slot.Slot-1]=slot.SignatureRefused; } Render();
    }
    public void ApplyAlive(bool alive)
    {
        if (_alive == alive) return;
        _alive = alive;
        _status.Text = alive ? "Эхо готовы продолжить путь." : "Эхо ждут вашего восстановления.";
    }
    public void Result(EchoSignatureResult value)
    {
        if (value.Slot is <1 or >NetworkConstants.MaxActiveEchoes ||
            value.Sequence!=_sequence && _issuedSignatureBySlot[value.Slot-1]!=value.Sequence) return;
        SignatureResult(value);
        _pendingSignatureSlots[value.Slot-1]=false;_pending=_pendingSignatureSlots.Any(p=>p); _cooldowns[value.Slot - 1] = value.CooldownSeconds;
        _status.Text=value.Outcome switch
        {
            EchoCommandOutcome.Accepted=>"Команда Эхо принята",EchoCommandOutcome.NotOwned=>"В этом месте нет активного Эхо",
            EchoCommandOutcome.Cooldown=>"Способность ещё восстанавливается",EchoCommandOutcome.OutOfRange=>"Цель слишком далеко от Эхо",
            EchoCommandOutcome.Blocked=>"Путь к цели перекрыт",EchoCommandOutcome.RateLimited=>"Слишком частые команды",
            EchoCommandOutcome.Refused=>"Эхо сейчас отказывается применять эту способность",
            EchoCommandOutcome.NoMana=>"У Эхо не хватает маны",EchoCommandOutcome.NoStamina=>"У Эхо не хватает выносливости",
            EchoCommandOutcome.NoHealth=>"У Эхо не хватает HP для оплаты навыка",
            EchoCommandOutcome.Busy=>"Эхо выполняет другое действие",EchoCommandOutcome.NeedsWeapon=>"Эхо требуется исправное оружие",
            EchoCommandOutcome.Capacity=>"Сейчас нельзя создать ещё один эффект",
            _=>"Сейчас нельзя применить способность Эхо"
        };
    }
    public override void _Process(double delta)
    {
        UpdateSignatureInput(delta);
        _hud.Visible=_known.Count>0 && _loadout.Slots is { Count: > 0 } && !GameUi.GameplayModalOpen;
        _readiness.Visible=_known.ContainsKey(_selected)&&(_roster is null||_roster.Value.Entries.Any(e=>e.Handle==_selectedOwned&&e.Availability==EchoAvailability.Active));
        _readiness.Text=!_alive?"Ожидает вашего восстановления":_refused[_selected-1]?"Отказывается применять способность":_cooldowns[_selected-1]>0?$"Готовность через {_cooldowns[_selected-1]:0.0} с":"Способность готова";
        for (var i = 0; i < _buttons.Length; i++)
        {
            _cooldowns[i] = Math.Max(0, _cooldowns[i] - delta);
            _buttons[i].Disabled=false;
            _buttons[i].Text =  $"{(_known.TryGetValue((byte)(i+1),out var echo)?echo.State.Name:$"Эхо {i+1}")}\n{Keys[i]} · "+(_refused[i]?"Отказ":_cooldowns[i]>0 ? $"{_cooldowns[i]:0.0}с" : "Готово");
        }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if(GameUi.GameplayModalOpen) return;
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if(key.PhysicalKeycode==Key.F3) { Toggle(); GetViewport().SetInputAsHandled(); return; }
        byte slot = key.PhysicalKeycode switch { Key.T => 1, Key.Y => 2, Key.U => 3, _ => 0 };
        if (slot == 0) return;
        BeginSignatureAim(slot,key.PhysicalKeycode); GetViewport().SetInputAsHandled();
    }
    private void Request(byte slot)
    {
        CommitSignature(slot);
    }
}
