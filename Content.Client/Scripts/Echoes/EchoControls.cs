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
        detail.AddChild(GameUi.Button("Вернуться к приключению",_window.Close)); Render();
    }
    public void Toggle() { if(_window.Visible) _window.Close(); else { Render(); _window.Open(); } }
    public void Observe(EchoSpawn state,Mesh mesh,Material? material)
    { if(state.OwnerId!=_owner) return; _known[state.Slot]=(state,mesh,material); Render(); }
    public void Remove(NetworkEntityId id)
    { foreach(var key in _known.Where(p=>p.Value.State.EntityId==id).Select(p=>p.Key).ToArray()) _known.Remove(key); Render(); }
    private void Render()
    {
        foreach(var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
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
        foreach (var button in _buttons) button.Visible = false;
        foreach (var slot in value.Slots) { _buttons[slot.Slot - 1].Visible = true; _cooldowns[slot.Slot - 1] = slot.CooldownSeconds; } Render();
    }
    public void ApplyAlive(bool alive)
    {
        if (_alive == alive) return;
        _alive = alive;
        _status.Text = alive ? "Эхо готовы продолжить путь." : "Эхо ждут вашего восстановления.";
    }
    public void Result(EchoSignatureResult value)
    {
        if (value.Sequence != _sequence) return;
        _pending = false; _cooldowns[value.Slot - 1] = value.CooldownSeconds;
        _status.Text = value.Outcome == EchoCommandOutcome.Accepted ? "Способность Эхо применена" : $"Способность недоступна: {value.Outcome}";
    }
    public override void _Process(double delta)
    {
        _hud.Visible=_known.Count>0 && _loadout.Slots is { Count: > 0 } && !GameUi.GameplayModalOpen;
        _readiness.Visible=_known.ContainsKey(_selected);
        _readiness.Text=!_alive?"Ожидает вашего восстановления":_cooldowns[_selected-1]>0?$"Готовность через {_cooldowns[_selected-1]:0.0} с":"Способность готова";
        if(_pending && Time.GetTicksMsec()/1000d-_sentAt>10) { _pending=false; _status.Text="Нет ответа. Попробуйте снова."; }
        for (var i = 0; i < _buttons.Length; i++)
        {
            _cooldowns[i] = Math.Max(0, _cooldowns[i] - delta);
            _buttons[i].Disabled=false;
            _buttons[i].Text =  $"{(_known.TryGetValue((byte)(i+1),out var echo)?echo.State.Name:$"Эхо {i+1}")}\n{Keys[i]} · "+(_cooldowns[i]>0 ? $"{_cooldowns[i]:0.0}с" : "Готово");
        }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if(GameUi.GameplayModalOpen) return;
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if(key.PhysicalKeycode==Key.F3) { Toggle(); GetViewport().SetInputAsHandled(); return; }
        byte slot = key.PhysicalKeycode switch { Key.T => 1, Key.Y => 2, Key.U => 3, _ => 0 };
        if (slot == 0) return;
        Request(slot); GetViewport().SetInputAsHandled();
    }
    private void Request(byte slot)
    {
        if (GameUi.GameplayModalOpen || !_alive || _pending || !_buttons[slot - 1].Visible || _cooldowns[slot - 1] > 0) return;
        var player = GetParent().GetChildren().OfType<ProjectG.Gameplay.PlayerController>().FirstOrDefault(p=>p.EntityId==_owner);
        if (player is null || !player.TryCursorSurface(out var aim,out var height)) return;
        _sequence++; if (_sequence == 0) _sequence++;
        _pending = true; _sentAt=Time.GetTicksMsec()/1000d;
        _network.SendEchoSignature(new(_sequence, _network.LatestServerTick, slot, aim, height));
    }
}
