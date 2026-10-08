using Content.Shared.Network;
using Godot;
using ProjectG.Networking;

namespace ProjectG.Items;

/// <summary>AOI-controlled visuals; G under the cursor sends intent, never moves or grants an item locally.</summary>
public partial class GroundItemPresentation : Node3D
{
    private NetworkClient _network = null!;
    private readonly Dictionary<ulong, Node3D> _items = new();
    private uint _sequence;
    private uint? _pending;
    private readonly BoxMesh _mesh = new() { Size = new(.45f, .25f, .45f) };
    private readonly StandardMaterial3D _material = new() { AlbedoColor = new Color(1, .8f, .2f) };
    public void Initialize(NetworkClient network){_network=network;network.PvpLootReceived+=Loot;}
    public override void _ExitTree(){if(_network is not null)_network.PvpLootReceived-=Loot;}
    private void Loot(PvpLootState s){if(_items.TryGetValue(s.Handle,out var n)&&n.GetChild(1) is Label3D label)label.Text+="\nPvP-тег • G: 5 секунд • исчезнет через "+Math.Ceiling(s.RemainingSeconds/60)+" мин";}
    public void Spawn(GroundItemSpawn state)
    {
        if (_items.ContainsKey(state.Handle)) return;
        if (_items.Count >= 256) { GD.PushError("Ground presentation budget exceeded."); return; }
        var item = new Node3D { Position = new(state.Position.X, .2f, state.Position.Y) };
        AddChild(item); _items.Add(state.Handle, item);
        item.AddChild(new MeshInstance3D { Mesh = _mesh, MaterialOverride = _material });
        item.AddChild(new Label3D
        {
            Text = $"{state.Name} [G]", Position = new(0, .65f, 0), FontSize = 28,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = false
        });
    }
    public void Despawn(GroundItemDespawn state)
    {
        if (_items.Remove(state.Handle, out var item)) item.QueueFree();
    }
    public void Result(PickupResult result)
    {
        if (_pending == result.Sequence) _pending = null;
    }
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.G } || _pending is not null) return;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return;
        var mouse = GetViewport().GetMousePosition(); var origin = camera.ProjectRayOrigin(mouse); var direction = camera.ProjectRayNormal(mouse);
        if (Mathf.Abs(direction.Y) < .0001f) return;
        var distance = -origin.Y / direction.Y; if (distance <= 0) return;
        var cursor = origin + direction * distance;
        ulong selected = 0; var best = 1.5f * 1.5f;
        // Bounded scan only on a manual input event, not every frame. Server checks actual distance/LOS.
        foreach (var (handle, item) in _items)
        {
            var offset = item.GlobalPosition - cursor; offset.Y = 0;
            var squared = offset.LengthSquared();
            if (squared < best) { selected = handle; best = squared; }
        }
        if (selected == 0) return;
        if (++_sequence == 0) _sequence++;
        _pending = _sequence; _network.SendPickup(new(_sequence, selected)); GetViewport().SetInputAsHandled();
    }
}
