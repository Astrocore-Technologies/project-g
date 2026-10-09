using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using NumericsVector2 = System.Numerics.Vector2;

namespace ProjectG.Combat;

/// <summary>Owner-only input and feedback. Resource values come exclusively from the server.</summary>
public partial class AbilityPresentation : Node3D
{
    private readonly Label3D _label = new() { Position = new(0, 2.4f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
    private PlayerController _player = null!;
    private NetworkClient _network = null!;
    private AbilityLoadout _loadout;
    private double _receivedAt;
    private double _pendingAt;
    private uint _sequence;
    private uint _pending;
    private AbilityEffectVisual? _prediction;
    private string _feedback = "";
    private static readonly Key[] BarKeys = [Key.Q,Key.W,Key.E,Key.R,Key.A,Key.S,Key.D,Key.F];
    private readonly ushort[] _bar = new ushort[8];
    public void ApplyProgression(ProgressionState value)
    {
        Array.Clear(_bar);
        foreach (var skill in value.Skills) if (skill.Slot != 0) _bar[skill.Slot-1] = skill.Id;
    }

    public void Initialize(PlayerController player, NetworkClient network)
    {
        _player = player; _network = network;
        AddChild(_label);
    }

    public void ApplyLoadout(AbilityLoadout loadout)
    {
        if (loadout.EntityId != _player.EntityId) return;
        _loadout = loadout; _receivedAt = Now();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if(ProjectG.UI.GameUi.GameplayModalOpen) return;
        if (!_player.IsAlive || @event is not InputEventKey { Pressed: true, Echo: false } key || _pending != 0 || _loadout.Abilities is null) return;
        var index = Array.IndexOf(BarKeys,key.PhysicalKeycode);
        if (key.PhysicalKeycode != Key.Space && index < 0) return;
        AbilityProfile? selected = null;
        foreach (var slot in _loadout.Abilities)
            if (key.PhysicalKeycode == Key.Space ? slot.Form == AbilityForm.Dash : slot.Id == _bar[index]) { selected = slot; break; }
        if (selected is not { } profile || profile.ReadyInSeconds > Now() - _receivedAt || _loadout.Mana < profile.ManaCost) return;
        var form = profile.Form;
        if (form==AbilityForm.Dash && _network.LatestDefense is { } defense && defense.Stamina<defense.DodgeCost) return;
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var mouse = GetViewport().GetMousePosition();
        var origin = camera.ProjectRayOrigin(mouse); var ray = camera.ProjectRayNormal(mouse);
        if (Mathf.Abs(ray.Y) < 0.0001f || -origin.Y / ray.Y <= 0) return;
        var point = origin + ray * (-origin.Y / ray.Y);
        var position = _player.PredictedPosition;
        var aim = new NumericsVector2(point.X, point.Z);
        var offset = aim - position;
        if (offset.LengthSquared() < 0.000001f) return;
        var direction = NumericsVector2.Normalize(offset);
        if (form == AbilityForm.GroundArea && offset.LengthSquared() > profile.Range * profile.Range) return;
        if (++_sequence == 0) ++_sequence;
        if (form == AbilityForm.Dash && !_player.PredictDash(_sequence, direction, profile.Range, profile.Speed)) return;
        _pending = _sequence; _pendingAt = Now(); _feedback = "";
        _prediction = new AbilityEffectVisual();
        // World-space preview is replaced by the single confirmed effect, not duplicated.
        GetTree().CurrentScene.AddChild(_prediction);
        _prediction.Apply(new(1, _player.EntityId, _sequence, _network.LatestServerTick, profile.Id, form,
            AbilityPhase.Telegraph, position, form == AbilityForm.GroundArea ? aim : position, direction,
            profile.Radius, profile.Speed, (float)profile.CastSeconds), predicted: true);
        _network.SendAbility(new(_sequence, _network.LatestServerTick, profile.Id, form == AbilityForm.GroundArea ? aim : direction));
        GetViewport().SetInputAsHandled();
    }

    public void Confirm(AbilityEffectState state)
    {
        if (state.ActorId != _player.EntityId || state.Sequence != _pending) return;
        ClearPreview();
        _pending = 0;
    }

    public void ApplyResult(AbilityResult result)
    {
        if (result.Sequence != _pending) return;
        if (result.Outcome == AbilityOutcome.Accepted) return; // Authoritative effect replaces the preview.
        _player.RejectDash(result.Sequence);
        _feedback = result.Outcome.ToString(); _pending = 0;
        ClearPreview();
    }

    public override void _Process(double delta)
    {
        if (_pending != 0 && Now() - _pendingAt > 2)
        {
            _player.RejectDash(_pending); _pending = 0; ClearPreview();
        }
        if (_loadout.Abilities is null) return;
        _label.Text = _feedback;
    }

    public override void _ExitTree() => ClearPreview();
    private void ClearPreview()
    {
        if (GodotObject.IsInstanceValid(_prediction)) _prediction!.QueueFree();
        _prediction = null;
    }
    private static double Now() => Time.GetTicksMsec() / 1000d;
}
