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
        var panel = new VBoxContainer { Position = new(16, 80) }; AddChild(panel);
        for (var i = 0; i < _buttons.Length; i++)
        {
            var slot = (byte)(i + 1);
            var button = new Button { Visible = false };
            button.Pressed += () => Request(slot);
            panel.AddChild(button); _buttons[i] = button;
        }
        _status = new Label { Text = "Мира: рядом. Помогу, когда понадобится." }; panel.AddChild(_status);
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
            _buttons[i].Text = $"Signature Эхо {i + 1} [{Keys[i]}] {_cooldowns[i]:0.0}s";
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
