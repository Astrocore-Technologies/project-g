using Content.Shared.Network;
using Godot;
using ProjectG.Networking;

namespace ProjectG.Items;

/// <summary>Owner-only UI. Buttons send intentions; displayed equipment changes only on server state.</summary>
public partial class InventoryPresentation : CanvasLayer
{
    private NetworkClient _network = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _rows = null!;
    private Label _status = null!;
    private uint _sequence;
    private bool _waiting;
    private Button _revive = null!;
    private uint _reviveSequence;
    private bool _awaitingRevive;
    public void Initialize(NetworkClient network)
    {
        _network = network;
        _panel = new PanelContainer { Visible=false }; AddChild(_panel);
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _panel.OffsetLeft = -275; _panel.OffsetRight = -15; _panel.OffsetTop = 15;
        var content = new VBoxContainer(); _panel.AddChild(content);
        content.AddChild(new Label { Text = "Инвентарь [I] — 8 мест" });
        _rows = new VBoxContainer(); content.AddChild(_rows);
        _status = new Label { Text = "Ожидание сервера..." }; content.AddChild(_status);
        _revive = new Button { Text = "Оживить (Development)", Visible = network.CanDevelopmentRevive, Disabled = true };
        content.AddChild(_revive); _revive.Pressed += RequestRevive;
    }
    public void ApplyAlive(bool alive)
    {
        _revive.Disabled = alive || !_network.CanDevelopmentRevive;
        if (alive && _awaitingRevive) { _awaitingRevive = false; _status.Text = "Персонаж оживлён"; }
    }
    private void RequestRevive()
    {
        if (_revive.Disabled) return;
        if (++_reviveSequence == 0) _reviveSequence++;
        _revive.Disabled = true; _awaitingRevive = true; _status.Text = "Ожидание оживления...";
        _network.SendDevelopmentRevive(new(_reviveSequence));
    }
    public void Apply(InventoryState state)
    {
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        foreach (var item in state.Items)
        {
            var row = new VBoxContainer(); _rows.AddChild(row);
            row.AddChild(new Label { Text = $"{item.Name}{(item.Equipped ? " [надето]" : "")}" });
            row.AddChild(new Label { Text = $"ATK {item.AttackBonus:+0.##;-0.##;0}  DEF {item.DefenseBonus:+0.##;-0.##;0}  HP {item.HealthBonus:+0.##;-0.##;0}" });
            var button = new Button { Text = item.Equipped ? "Снять" : "Надеть", Disabled = _waiting };
            button.Pressed += () => Request(item);
            row.AddChild(button);
        }
        if (state.Items.Count == 0) _rows.AddChild(new Label { Text = "Пусто" });
    }
    private void Request(InventoryEntry item)
    {
        if (_waiting) return;
        if (++_sequence == 0) _sequence++;
        _waiting = true; _status.Text = "Ожидание подтверждения...";
        SetButtons(true);
        _network.SendInventory(new(_sequence, item.Equipped ? InventoryAction.Unequip : InventoryAction.Equip, item.Handle));
    }
    public void Result(InventoryResult result)
    {
        if (result.Sequence != _sequence) return;
        _waiting = false; SetButtons(false);
        _status.Text = result.Outcome == InventoryOutcome.Accepted ? "Подтверждено сервером" : $"Отклонено: {result.Outcome}";
    }
    public void PickupResult(PickupResult result) => _status.Text = result.Outcome == PickupOutcome.Accepted
        ? "Предмет подобран" : $"Подбор отклонён: {result.Outcome}";
    private void SetButtons(bool disabled)
    {
        foreach (var row in _rows.GetChildren())
            foreach (var node in row.GetChildren()) if (node is Button button) button.Disabled = disabled;
    }
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.I })
        {
            _panel.Visible = !_panel.Visible; GetViewport().SetInputAsHandled();
        }
    }
}
