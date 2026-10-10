using Content.Shared.Network;
using Content.Shared.Navigation;
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
    private readonly AbilityAreaVisual _area = new();
    private Key _aimKey;
    private ushort _aimId;
    private double _areaRefresh;
    private CombatPresentation? _combat;
    private Window? _focusWindow;
    public bool IsAiming => _aimId != 0;
    public AbilityAreaVisual AreaVisual => _area;
    public void ApplyProgression(ProgressionState value)
    {
        Array.Clear(_bar);
        foreach (var skill in value.Skills) if (skill.Slot != 0) _bar[skill.Slot-1] = skill.Id;
    }

    public void Initialize(PlayerController player, NetworkClient network)
    {
        _player = player; _network = network;
        _focusWindow = GetWindow(); _focusWindow.FocusExited += CancelAim;
        AddChild(_label); AddChild(_area);
    }

    public void ApplyLoadout(AbilityLoadout loadout)
    {
        if (loadout.EntityId != _player.EntityId) return;
        _loadout = loadout; _receivedAt = Now();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if(ProjectG.UI.GameUi.GameplayModalOpen) return;
        if (!_player.IsAlive || !_network.RegionActive || @event is not InputEventKey { Pressed: true, Echo: false } key || _pending != 0 || IsAiming || _loadout.Abilities is null) return;
        var index = Array.IndexOf(BarKeys,key.PhysicalKeycode);
        if (key.PhysicalKeycode != Key.Space && index < 0) return;
        if (key.PhysicalKeycode == Key.Space && _player.Control is CombatControlPhase.Airborne or CombatControlPhase.KnockedDown) return;
        AbilityProfile? selected = null;
        foreach (var slot in _loadout.Abilities)
            if (key.PhysicalKeycode == Key.Space ? slot.Form == AbilityForm.Dash : slot.Id == _bar[index]) { selected = slot; break; }
        if (selected is not { } profile) return;
        _aimKey = key.PhysicalKeycode; _aimId = profile.Id;
        RefreshArea(); GetViewport().SetInputAsHandled();
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsAiming) return;
        if (@event is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape } ||
            @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { CancelAim(); GetViewport().SetInputAsHandled(); return; }
        if (@event is not InputEventKey { Pressed: false } key || key.PhysicalKeycode != _aimKey) return;
        var id = _aimId; CancelAim();
        // Only releasing the key creates an intent; holding and cancelling never spends resources.
        if (FindProfile(id) is { } profile) Commit(profile);
        GetViewport().SetInputAsHandled();
    }

    private AbilityProfile? FindProfile(ushort id)
    {
        if (_loadout.Abilities is not null) foreach (var profile in _loadout.Abilities) if (profile.Id == id) return profile;
        return null;
    }
    private string Unavailable(AbilityProfile profile)
    {
        if (!_player.IsAlive || !_network.RegionActive || ProjectG.UI.GameUi.GameplayModalOpen) return "Сейчас нельзя применить навык";
        if (profile.ReadyInSeconds > Now() - _receivedAt) return "Навык перезаряжается";
        if (_loadout.Mana < profile.ManaCost) return "Не хватает маны";
        if (profile.Availability != AbilityAvailability.Ready)
            return profile.Availability == AbilityAvailability.NeedsSword ? "Нужен надетый исправный меч" : "Сначала парируйте ближний удар";
        if (_network.LatestDefense is { } defense && defense.Stamina < profile.StaminaCost) return "Не хватает выносливости";
        return "";
    }

    private bool TryAim(AbilityProfile profile, out NumericsVector2 aim, out float aimHeight,
        out NumericsVector2 direction, out float directionY, out float dashDistance, out System.Numerics.Vector3 dashEnd)
    {
        var foot = _player.PredictedFoot;
        aim = _player.PredictedPosition; aimHeight = foot.Y; direction = NumericsVector2.UnitY;
        directionY = dashDistance = 0; dashEnd = foot;
        if (profile.Area.Shape == AbilityAreaShape.Self) return true;
        if (!_player.TryCursorSurface(out aim, out aimHeight)) return false;
        var form = profile.Form;
        var offset = aim - _player.PredictedPosition;
        direction = offset.LengthSquared()<.000001f ? NumericsVector2.UnitY : NumericsVector2.Normalize(offset);
        if (form == AbilityForm.Projectile)
        {
            var length = MathF.Sqrt(offset.LengthSquared() + MathF.Pow(aimHeight - foot.Y, 2));
            if (length < .0001f) return false;
            direction = offset / length; directionY = (aimHeight - foot.Y) / length;
        }
        if (form == AbilityForm.Dash)
        {
            dashDistance = Math.Min(profile.Range, offset.Length());
            if (dashDistance < .001f || !SurfaceDash.TryDestination(_player.Navigation, foot, direction, dashDistance, out var end, out var height)) return false;
            dashEnd = new(end.X, height, end.Y);
        }
        return true;
    }

    private void RefreshArea()
    {
        if (FindProfile(_aimId) is not { } profile) { CancelAim(); return; }
        var reason = Unavailable(profile);
        if (!TryAim(profile, out var aim, out var height, out var direction, out var dy, out _, out var dashEnd))
        { _area.HideArea(); _feedback = "Наведите курсор на поверхность"; return; }
        if (profile.Form == AbilityForm.GroundArea && !_player.CanAttack(new(aim.X, height, aim.Y), profile.Range)) reason = "Вне дальности или за препятствием";
        _area.Show(profile, _player.Navigation, _player.PredictedFoot, new(aim.X, height, aim.Y), direction, dy, dashEnd, reason.Length == 0);
        _feedback = reason.Length > 0 ? reason : profile.Area.Shape == AbilityAreaShape.Self
            ? "На себя · отпустите клавишу · Esc/ПКМ отмена" : "Отпустите клавишу для применения · Esc/ПКМ отмена";
    }

    private void Commit(AbilityProfile profile)
    {
        _feedback = Unavailable(profile); if (_feedback.Length > 0 || _pending != 0) return;
        if (!TryAim(profile, out var aim, out var aimHeight, out var direction, out var directionY, out var dashDistance, out _)) return;
        var form = profile.Form; var position = _player.PredictedPosition;
        if (form == AbilityForm.GroundArea && !_player.CanAttack(new(aim.X, aimHeight, aim.Y), profile.Range)) { _feedback = "Вне дальности или за препятствием"; return; }
        if (++_sequence == 0) ++_sequence;
        var delayed = _player.RecoveryRemaining > 0;
        if (form == AbilityForm.Dash && !delayed && !_player.PredictDash(_sequence, direction, dashDistance, profile.Speed)) return;
        if (profile.Area.Stationary)
        {
            // Combat state can arrive after the player spawn; resolve once when the first stationary skill is used.
            _combat ??= _player.GetNodeOrNull<CombatPresentation>("CombatPresentation");
            _combat?.CancelAutoAttack(); _player.StopMovement();
        }
        _pending = _sequence; _pendingAt = Now(); _feedback = "";
        if (!delayed) _player.SwordAnimation.PredictAbility(_sequence, profile.Id, direction,
            form == AbilityForm.Dash ? dashDistance / profile.Speed : profile.CastSeconds);
        _prediction = new AbilityEffectVisual();
        // World-space preview is replaced by the single confirmed effect, not duplicated.
        GetTree().CurrentScene.AddChild(_prediction);
        _prediction.Apply(new(1, _player.EntityId, _sequence, _network.LatestServerTick, profile.Id, form,
            AbilityPhase.Telegraph, position, form == AbilityForm.GroundArea ? aim : position, direction,
            profile.Form == AbilityForm.Melee ? profile.Range : profile.Radius, profile.Speed, (float)profile.CastSeconds, _player.PredictedFoot.Y, form == AbilityForm.GroundArea ? aimHeight : _player.PredictedFoot.Y, directionY, profile.Area), predicted: true);
        _network.SendAbility(new(_sequence, _network.LatestServerTick, profile.Id, form == AbilityForm.GroundArea ? aim : direction, dashDistance, form == AbilityForm.GroundArea ? aimHeight : 0, directionY));
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
        if (result.Outcome == AbilityOutcome.Buffered) { _feedback="Действие подготовлено"; ClearPreview(); _player.SwordAnimation.RejectAbility(result.Sequence); return; }
        if (result.Outcome == AbilityOutcome.Accepted) return; // Authoritative effect replaces the preview.
        _player.RejectDash(result.Sequence);
        _player.SwordAnimation.RejectAbility(result.Sequence);
        _feedback = result.Outcome switch { AbilityOutcome.NoStamina=>"Не хватает выносливости",AbilityOutcome.NeedsSword=>"Нужен надетый исправный меч",AbilityOutcome.NeedsParry=>"Нужно успешное парирование",AbilityOutcome.Cooldown=>"Навык перезаряжается",AbilityOutcome.Busy=>"Завершите текущее действие",AbilityOutcome.InvalidState=>"Сейчас нельзя применить навык",_=>"Навык сейчас недоступен" }; _pending = 0;
        ClearPreview();
    }

    public override void _Process(double delta)
    {
        if (IsAiming)
        {
            if (!_player.IsAlive || !_network.RegionActive || ProjectG.UI.GameUi.GameplayModalOpen) CancelAim();
            else if ((_areaRefresh += delta) >= .05) { _areaRefresh = 0; RefreshArea(); }
        }
        if (_pending != 0 && Now() - _pendingAt > 2)
        {
            _player.RejectDash(_pending); _player.SwordAnimation.RejectAbility(_pending); _pending = 0; ClearPreview();
        }
        if (_loadout.Abilities is null) return;
        _label.Text = _feedback;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_focusWindow)) _focusWindow!.FocusExited -= CancelAim;
        CancelAim(); ClearPreview();
    }
    private void CancelAim() { _aimId = 0; _aimKey = Key.None; _area.HideArea(); _feedback = ""; }
    private void ClearPreview()
    {
        if (GodotObject.IsInstanceValid(_prediction)) _prediction!.QueueFree();
        _prediction = null;
    }
    private static double Now() => Time.GetTicksMsec() / 1000d;
}
