using Content.Shared.Network;
using Godot;
using ProjectG.Networking;

namespace ProjectG.Echoes;

/// <summary>Three owner-only commands; local timers are presentation, not authority.</summary>
public partial class EchoControls : CanvasLayer
{
    private NetworkClient _network = null!;
    private readonly Button[] _buttons = new Button[NetworkConstants.MaxActiveEchoes];
    private readonly double[] _cooldowns = new double[NetworkConstants.MaxActiveEchoes];
    private Label _status = null!;
    private bool _alive = true, _pending;
    private uint _sequence;
    private static readonly string[] Keys = ["T", "Y", "U"];
    public void Initialize(NetworkClient network)
    {
        _network = network;
        Layer = 3;
        var panel = new VBoxContainer { Theme=ProjectG.UI.GameUi.CreateTheme(), AnchorLeft=.5f,AnchorRight=.5f,AnchorTop=1,AnchorBottom=1,OffsetLeft=-230,OffsetRight=230,OffsetTop=-162,OffsetBottom=-100 }; AddChild(panel);
        var commands = new HBoxContainer { Alignment=BoxContainer.AlignmentMode.Center }; panel.AddChild(commands);
        for (var i = 0; i < _buttons.Length; i++)
        {
            var slot = (byte)(i + 1);
            var button = new Button { Visible = false, CustomMinimumSize=new(144,32), FocusMode=Control.FocusModeEnum.None, TooltipText="Направьте курсор в мир и нажмите клавишу Эхо" };
            button.Pressed += () => Request(slot);
            commands.AddChild(button); _buttons[i] = button;
        }
        _status = ProjectG.UI.GameUi.Text("T / Y / U — способности Эхо",12); _status.HorizontalAlignment=HorizontalAlignment.Center; panel.AddChild(_status);
    }
    public void Apply(EchoLoadout value)
    {
        foreach (var button in _buttons) button.Visible = false;
        foreach (var slot in value.Slots) { _buttons[slot.Slot - 1].Visible = true; _cooldowns[slot.Slot - 1] = slot.CooldownSeconds; }
    }
    public void ApplyAlive(bool alive)
    {
        if (_alive == alive) return;
        _alive = alive;
        _status.Text = alive ? "Мира: снова в путь." : "Мира: жду твоего восстановления.";
    }
    public void Result(EchoSignatureResult value)
    {
        if (value.Sequence != _sequence) return;
        _pending = false; _cooldowns[value.Slot - 1] = value.CooldownSeconds;
        _status.Text = value.Outcome == EchoCommandOutcome.Accepted ? "Мира: прикрываю!" : $"Signature: {value.Outcome}";
    }
    public override void _Process(double delta)
    {
        for (var i = 0; i < _buttons.Length; i++)
        {
            _cooldowns[i] = Math.Max(0, _cooldowns[i] - delta);
            _buttons[i].Disabled = !_alive || _pending || _cooldowns[i] > 0;
            _buttons[i].Text = $"{Keys[i]} · Эхо {i + 1} · "+(_cooldowns[i]>0 ? $"{_cooldowns[i]:0.0}с" : "Готово");
        }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        byte slot = key.PhysicalKeycode switch { Key.T => 1, Key.Y => 2, Key.U => 3, _ => 0 };
        if (slot == 0) return;
        Request(slot); GetViewport().SetInputAsHandled();
    }
    private void Request(byte slot)
    {
        if (!_alive || _pending || !_buttons[slot - 1].Visible || _cooldowns[slot - 1] > 0) return;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return;
        var mouse = GetViewport().GetMousePosition();
        var origin = camera.ProjectRayOrigin(mouse); var ray = camera.ProjectRayNormal(mouse);
        if (Math.Abs(ray.Y) < .0001f) return;
        var distance = -origin.Y / ray.Y; if (distance < 0) return;
        var aim = origin + ray * distance;
        _sequence++; if (_sequence == 0) _sequence++;
        _pending = true;
        _network.SendEchoSignature(new(_sequence, _network.LatestServerTick, slot, new(aim.X, aim.Z)));
    }
}
