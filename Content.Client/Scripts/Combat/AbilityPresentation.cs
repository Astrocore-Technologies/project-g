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
    private bool _pendingBuffered;
    private ushort _pendingToggleId;
    private ushort _pendingSustainSkill;
    private int _pendingSustainCharges;
    private bool _requestedToggleOn;
    private uint _cancelSequence;
    private uint _lastCancelTarget;
    private uint _executingSequence;
    private ulong _executingExecutionId,_heldExecutionId;
    private uint _lastFinishedSequence;
    private readonly Dictionary<uint,Content.Shared.Combat.CombatActionForm> _sustainedSequences=new();
    private readonly Queue<uint> _sustainedOrder=new();
    private uint _heldSequence,_controlSequence;
    private uint _lastChargeSequence;
    private AbilityControlCommand _lastHeldAim;
    private double _chargeStartedAt,_heldAimRefresh;
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
        AdvanceSequenceFloor(loadout.LastInputSequence);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if(ProjectG.UI.GameUi.GameplayModalOpen) return;
        if (!_player.IsAlive || !_network.RegionActive || @event is not InputEventKey { Pressed: true, Echo: false } key || _pending != 0 && !_pendingBuffered && _pendingToggleId==0 || IsAiming && _heldSequence==0 || _loadout.Abilities is null) return;
        var index = Array.IndexOf(BarKeys,key.PhysicalKeycode);
        if (key.PhysicalKeycode != Key.Space && index < 0) return;
        if (key.PhysicalKeycode == Key.Space && _player.Control is CombatControlPhase.Airborne or CombatControlPhase.KnockedDown) return;
        AbilityProfile? selected = null;
        foreach (var slot in _loadout.Abilities)
            if (key.PhysicalKeycode == Key.Space ? slot.Form == AbilityForm.Dash : slot.Id == _bar[index]) { selected = slot; break; }
        if (selected is not { } profile) return;
        if(_pending!=0 && !_pendingBuffered && profile.Id!=_pendingToggleId)return;
        if(IsAiming)CancelAim();
        _aimKey = key.PhysicalKeycode; _aimId = profile.Id;
        RefreshArea();
        if(profile.ExecutionForm==Content.Shared.Combat.CombatActionForm.Toggle){Commit(profile);CancelAim();}
        if(profile.ExecutionForm is Content.Shared.Combat.CombatActionForm.Charged or Content.Shared.Combat.CombatActionForm.Hold)
        {
            Commit(profile);
            if(_pending!=0){_heldSequence=_pending;_chargeStartedAt=Now();_heldAimRefresh=0;}
            else {var reason=_feedback;CancelAim();_feedback=reason;}
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Input(InputEvent @event)
    {
        if(!IsAiming && @event is InputEventKey {Pressed:true,Echo:false,PhysicalKeycode:Key.Escape} &&
            !ProjectG.UI.GameUi.GameplayModalOpen && _network.RegionActive && _player.IsAlive)
        {
            var sequence=_pending!=0 ? _pending : _executingSequence;
            if(sequence==0) return;
            CancelServerAction(sequence);
            GetViewport().SetInputAsHandled(); return;
        }
        if (!IsAiming) return;
        if (@event is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape } ||
            @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { CancelAim(); GetViewport().SetInputAsHandled(); return; }
        if (@event is not InputEventKey { Pressed: false } key || key.PhysicalKeycode != _aimKey) return;
        var id = _aimId;var charged=_heldSequence;
        if(charged!=0 && FindProfile(id) is {} chargedProfile)SendHeldControl(chargedProfile,AbilityControlKind.Release);
        _heldSequence=0;CancelAim();
        // Only releasing the key creates an intent; holding and cancelling never spends resources.
        if (charged==0 && FindProfile(id) is { } profile) Commit(profile);
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
        if(profile.ExecutionForm==Content.Shared.Combat.CombatActionForm.Toggle && profile.ToggleState!=Content.Shared.Combat.ToggleState.Off)return "";
        if(profile.PendingEndCharges>0 && profile.Charges==0)return "Навык поддерживается";
        if (profile.ReadyInSeconds > Now() - _receivedAt) return "Навык перезаряжается";
        if (_loadout.Mana < profile.ManaCost) return "Не хватает маны";
        if(profile.ExecutionForm is Content.Shared.Combat.CombatActionForm.Channel or Content.Shared.Combat.CombatActionForm.Hold && profile.ChannelManaPerSecond>0 && _loadout.Mana<=profile.ManaCost)
            return "Не хватает маны для поддержания";
        if (profile.Availability != AbilityAvailability.Ready)
            return profile.Availability switch {AbilityAvailability.NeedsSword=>"Нужен надетый исправный меч",AbilityAvailability.NeedsWeapon=>"Нужно надетое исправное оружие",_=>"Сначала парируйте ближний удар"};
        if (_network.LatestDefense is { } defense && (defense.Stamina < profile.StaminaCost ||
            profile.ExecutionForm is Content.Shared.Combat.CombatActionForm.Channel or Content.Shared.Combat.CombatActionForm.Hold && profile.ChannelStaminaPerSecond>0 && defense.Stamina<=profile.StaminaCost)) return "Не хватает выносливости";
        return "";
    }

    private bool TryAim(AbilityProfile profile, out NumericsVector2 aim, out float aimHeight,
        out NumericsVector2 direction, out float directionY, out float dashDistance, out System.Numerics.Vector3 dashEnd)
    {
        var foot = _player.PredictedFoot;
        aim = _player.PredictedPosition; aimHeight = foot.Y; direction = NumericsVector2.UnitY;
        directionY = dashDistance = 0; dashEnd = foot;
        if (profile.Area.Shape == AbilityAreaShape.Self) return true;
        if (!(profile.Form is AbilityForm.Projectile or AbilityForm.Melee ? _player.TryCursorCombatPoint(out aim,out aimHeight) : _player.TryCursorSurface(out aim, out aimHeight))) return false;
        var form = profile.Form;
        var offset = aim - _player.PredictedPosition;
        direction = offset.LengthSquared()<.000001f ? NumericsVector2.UnitY : NumericsVector2.Normalize(offset);
        if (form is AbilityForm.Projectile or AbilityForm.Melee)
        {
            var originY=foot.Y+profile.AttackOriginHeight;
            var length = MathF.Sqrt(offset.LengthSquared() + MathF.Pow(aimHeight - originY, 2));
            if (length < .0001f) return false;
            direction = offset / length; directionY = (aimHeight - originY) / length;
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
        _combat??=_player.GetNodeOrNull<CombatPresentation>("CombatPresentation");
        _area.Show(profile, _player.Navigation, _player.PredictedFoot, new(aim.X, height, aim.Y), direction, dy, dashEnd, reason.Length == 0,_combat?.Facing);
        _feedback = reason.Length > 0 ? reason : profile.Area.Shape == AbilityAreaShape.Self
            ? "На себя · отпустите клавишу · Esc/ПКМ отмена" : "Отпустите клавишу для применения · Esc/ПКМ отмена";
    }

    private void Commit(AbilityProfile profile)
    {
        var toggle=profile.ExecutionForm==Content.Shared.Combat.CombatActionForm.Toggle;
        var sameToggle=toggle && _pendingToggleId==profile.Id;
        var deactivate=toggle && (sameToggle?_requestedToggleOn:profile.ToggleState!=Content.Shared.Combat.ToggleState.Off);
        if (_pending!=0 && !_pendingBuffered && !sameToggle)return;
        if(deactivate)
        {
            if(++_sequence==0)++_sequence;_pending=_sequence;_pendingAt=Now();_pendingBuffered=false;
            _pendingToggleId=profile.Id;_requestedToggleOn=false;
            _network.SendAbility(new(_sequence,_network.LatestServerTick,profile.Id,NumericsVector2.UnitY,Intent:AbilityIntent.Deactivate));return;
        }
        _feedback = Unavailable(profile); if (_feedback.Length > 0) return;
        if (!TryAim(profile, out var aim, out var aimHeight, out var direction, out var directionY, out var dashDistance, out _)) return;
        var form = profile.Form; var position = _player.PredictedPosition;
        if (form == AbilityForm.GroundArea && !_player.CanAttack(new(aim.X, aimHeight, aim.Y), profile.Range)) { _feedback = "Вне дальности или за препятствием"; return; }
        if (++_sequence == 0) ++_sequence;
        _heldExecutionId=0;
        var delayed = _player.RecoveryRemaining > 0;
        if (form == AbilityForm.Dash && !delayed && !_player.PredictDash(_sequence, direction, dashDistance, profile.Speed)) return;
        _combat ??= _player.GetNodeOrNull<CombatPresentation>("CombatPresentation");
        _combat?.CancelBasicAttack();
        // Navigation is cancelled by the accepted server action, not by a preview or a rejected request.
        _pending = _sequence; _pendingAt = Now(); _pendingBuffered=false; _feedback = "";
        _pendingSustainSkill=profile.ExecutionForm is Content.Shared.Combat.CombatActionForm.Channel or Content.Shared.Combat.CombatActionForm.Hold or Content.Shared.Combat.CombatActionForm.Toggle?profile.Id:(ushort)0;
        _pendingSustainCharges=profile.Charges;
        _pendingToggleId=toggle?profile.Id:(ushort)0;_requestedToggleOn=toggle;
        if (!delayed) _player.SwordAnimation.PredictAbility(_sequence, profile.Id, direction,
            form == AbilityForm.Dash ? profile.DodgeDurationSeconds : profile.CastSeconds,directionY);
        ClearPreview();_prediction = new AbilityEffectVisual{AimFacing=_combat?.Facing??NumericsVector2.UnitY};
        // World-space preview is replaced by the single confirmed effect, not duplicated.
        GetTree().CurrentScene.AddChild(_prediction);
        _prediction.Apply(new(1, _player.EntityId, _sequence, _network.LatestServerTick, profile.Id, form,
            AbilityPhase.Telegraph, position, form == AbilityForm.GroundArea ? aim : position, direction,
            profile.Form == AbilityForm.Melee ? profile.Range : profile.Radius, profile.Speed, (float)profile.CastSeconds,
            _player.PredictedFoot.Y+profile.AttackOriginHeight, form == AbilityForm.GroundArea ? aimHeight : _player.PredictedFoot.Y+profile.ProjectileLaunchHeight, directionY, profile.Area,profile.MeleeThickness,profile.GroundVolumeHeight), predicted: true);
        _network.SendAbility(new(_sequence, _network.LatestServerTick, profile.Id, form == AbilityForm.GroundArea ? aim : direction, dashDistance, form == AbilityForm.GroundArea ? aimHeight : 0, directionY));
        if(profile.ExecutionForm is Content.Shared.Combat.CombatActionForm.Charged or Content.Shared.Combat.CombatActionForm.Hold)
            _lastHeldAim=new(0,_sequence,_network.LatestServerTick,AbilityControlKind.Aim,form==AbilityForm.GroundArea?aim:direction,form==AbilityForm.GroundArea?aimHeight:0,directionY);
        GetViewport().SetInputAsHandled();
    }

    private void SendHeldControl(AbilityProfile profile,AbilityControlKind kind)
    {
        if(_heldSequence==0)return;
        if(TryAim(profile,out var aim,out var height,out var direction,out var dy,out _,out _) &&
            (profile.Form!=AbilityForm.GroundArea || _player.CanAttack(new(aim.X,height,aim.Y),profile.Range)))
            _lastHeldAim=new(0,_heldSequence,_network.LatestServerTick,kind,profile.Form==AbilityForm.GroundArea?aim:direction,
                profile.Form==AbilityForm.GroundArea?height:0,dy);
        else if(kind==AbilityControlKind.Aim)return;
        if(++_controlSequence==0)++_controlSequence;
        _network.SendAbilityControl(_lastHeldAim with {Sequence=_controlSequence,ActionSequence=_heldSequence,ObservedServerTick=_network.LatestServerTick,Kind=kind,ExecutionId=_heldExecutionId});
    }

    public void Confirm(AbilityEffectState state)
    {
        if(state.AbilityId==0)return; // Basic input has a separate sequence space and action acknowledgements.
        if(state.ActorId!=_player.EntityId) return;
        AdvanceSequenceFloor(state.Sequence);
        if(state.Phase==AbilityPhase.Finished)_lastFinishedSequence=state.Sequence;
        if(state.Phase is AbilityPhase.Channeling or AbilityPhase.Sustaining && _sustainedSequences.TryAdd(state.Sequence,FindProfile(state.AbilityId)?.ExecutionForm ?? Content.Shared.Combat.CombatActionForm.Channel))
        {
            _sustainedOrder.Enqueue(state.Sequence);
            while(_sustainedOrder.Count>16)_sustainedSequences.Remove(_sustainedOrder.Dequeue());
        }
        if(state.Phase==AbilityPhase.Charging)_lastChargeSequence=state.Sequence;
        if(state.Phase is AbilityPhase.Telegraph or AbilityPhase.Channeling or AbilityPhase.Charging)
        {_executingSequence=state.Sequence;_executingExecutionId=state.EffectId;}
        else if(_executingExecutionId==state.EffectId){_executingSequence=0;_executingExecutionId=0;}
        if(_heldSequence==state.Sequence && state.Phase is AbilityPhase.Telegraph or AbilityPhase.Channeling or AbilityPhase.Charging)
            _heldExecutionId=state.EffectId;
        if(_heldSequence==state.Sequence && !(FindProfile(state.AbilityId)?.ExecutionForm==Content.Shared.Combat.CombatActionForm.Hold
            ? state.Phase is AbilityPhase.Telegraph or AbilityPhase.Channeling : state.Phase==AbilityPhase.Charging))
        {_heldSequence=0;_heldExecutionId=0;CancelAim();}
        if (state.Sequence != _pending) return;
        ClearPreview();
        _pending = 0;
        _pendingToggleId=0;
    }

    public void ApplyResult(AbilityResult result)
    {
        // A funded channel may exhaust and finish before its first public state.
        // The private loadout precedes this result; a spent charge proves activation.
        var exhausted=result.Outcome is AbilityOutcome.NoMana or AbilityOutcome.NoStamina &&
            (_sustainedSequences.ContainsKey(result.Sequence) || result.Sequence==_pending && _pendingSustainSkill!=0 &&
                FindProfile(_pendingSustainSkill) is {} sustain && sustain.Charges<_pendingSustainCharges);
        var exhaustionFeedback=result.Outcome==AbilityOutcome.NoMana?"Поддержание прекращено: закончилась мана":"Поддержание прекращено: закончилась выносливость";
        if(result.Sequence==_lastChargeSequence && result.Outcome is AbilityOutcome.NoMana or AbilityOutcome.NoStamina &&
            (result.Sequence==_executingSequence || _executingSequence==0 && result.Sequence==_lastFinishedSequence))
            _feedback=result.Outcome==AbilityOutcome.NoMana?"Зарядка сорвана: не хватает маны":"Зарядка сорвана: не хватает выносливости";
        if(exhausted)_feedback=exhaustionFeedback;
        if(result.Outcome is AbilityOutcome.Cancelled or AbilityOutcome.CancelDenied)
        {
            if(result.Sequence!=_pending && result.Sequence!=_executingSequence && result.Sequence!=_lastCancelTarget) return;
            if(result.Sequence==_lastCancelTarget)_lastCancelTarget=0;
            if(result.Outcome==AbilityOutcome.Cancelled)
            {
                _feedback="Действие отменено"; _player.SwordAnimation.RejectAbility(result.Sequence);
                if(result.Sequence==_pending) { _pending=0;_pendingToggleId=0; ClearPreview(); }
                if(result.Sequence==_executingSequence){_executingSequence=0;_executingExecutionId=0;}
            }
            else _feedback="Сейчас нельзя отменить действие";
            return;
        }
        if (result.Sequence != _pending) return;
        if (result.Outcome == AbilityOutcome.Buffered) { _pendingBuffered=true; _feedback="Действие подготовлено"; ClearPreview(); _player.SwordAnimation.RejectAbility(result.Sequence); return; }
        if (result.Outcome == AbilityOutcome.Accepted) return; // Authoritative effect replaces the preview.
        if(_heldSequence==result.Sequence){_heldSequence=0;CancelAim();}
        _player.RejectDash(result.Sequence);
        _player.SwordAnimation.RejectAbility(result.Sequence);
        _feedback = exhausted?exhaustionFeedback:result.Outcome switch {AbilityOutcome.AlreadyActive=>"Навык уже включён",AbilityOutcome.NoHealth=>"Не хватает HP для оплаты навыка",AbilityOutcome.NoMana=>"Не хватает маны", AbilityOutcome.NoStamina=>"Не хватает выносливости",AbilityOutcome.NeedsWeapon=>"Нужно надетое исправное оружие",AbilityOutcome.NeedsSword=>"Нужен надетый исправный меч",AbilityOutcome.NeedsParry=>"Нужно успешное парирование",AbilityOutcome.Cooldown=>"Навык перезаряжается",AbilityOutcome.Busy=>"Завершите текущее действие",AbilityOutcome.InvalidState=>"Сейчас нельзя применить навык",_=>"Навык сейчас недоступен" }; _pending = 0;
        _pendingToggleId=0;
        ClearPreview();
    }

    public override void _Process(double delta)
    {
        if (IsAiming)
        {
            if (!_player.IsAlive || !_network.RegionActive || ProjectG.UI.GameUi.GameplayModalOpen) CancelAim();
            else if ((_areaRefresh += delta) >= .05)
            {
                _areaRefresh = 0; RefreshArea();
                if(_heldSequence!=0 && FindProfile(_aimId) is {} profile)
                {
                    _feedback=profile.ExecutionForm==Content.Shared.Combat.CombatActionForm.Hold ? "Удержание · отпустите клавишу для завершения" :
                        $"Зарядка {Math.Clamp((Now()-_chargeStartedAt)/profile.ChargeSeconds,0,1):P0} · отпустите клавишу · Esc/ПКМ отмена";
                    if((_heldAimRefresh+=.05)>=.1 && (profile.ChargeCanTurn || profile.SustainCanTurn))
                    {_heldAimRefresh=0;SendHeldControl(profile,AbilityControlKind.Aim);}
                }
            }
        }
        if (_pending != 0 && Now() - _pendingAt > 2)
        {
            _player.RejectDash(_pending); _player.SwordAnimation.RejectAbility(_pending); _pending = 0;_pendingToggleId=0; ClearPreview();
        }
        if (_loadout.Abilities is null) return;
        _label.Text = _feedback;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_focusWindow)) _focusWindow!.FocusExited -= CancelAim;
        CancelAim(); ClearPreview();
    }
    private void CancelAim()
    {
        if(_heldSequence!=0)
        {CancelServerAction(_heldSequence);_heldSequence=0;}
        _aimId = 0; _aimKey = Key.None; _area.HideArea(); _feedback = "";
    }
    private void CancelServerAction(uint sequence)
    {
        if(++_cancelSequence==0)++_cancelSequence;_lastCancelTarget=sequence;
        var execution=sequence==_executingSequence?_executingExecutionId:sequence==_heldSequence?_heldExecutionId:0;
        _network.SendActionCancel(new(_cancelSequence,sequence,execution));
    }
    private void AdvanceSequenceFloor(uint sequence)
    {if(sequence!=0 && (_sequence==0 || Content.Shared.Movement.MovementSimulation.IsSequenceNewer(sequence,_sequence)))_sequence=sequence;}
    private void ClearPreview()
    {
        if (GodotObject.IsInstanceValid(_prediction)) _prediction!.QueueFree();
        _prediction = null;
    }
    private static double Now() => Time.GetTicksMsec() / 1000d;
}
