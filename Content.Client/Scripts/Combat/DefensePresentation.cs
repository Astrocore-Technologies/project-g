using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.Gameplay;
using ProjectG.UI;
using V2 = System.Numerics.Vector2;
namespace ProjectG.Combat;

/// <summary>Owner guard input; leases prevent a lost key-up from leaving permanent protection.</summary>
public partial class DefensePresentation : Node
{
    private PlayerController _player = null!;
    private NetworkClient _network = null!;
    private PlayerHud _hud = null!;
    private uint _sequence;
    private bool _held;
    private bool _quickHeld;
    private readonly AbilityAreaVisual _quickArea = new();
    private readonly Label3D _quickLabel = new() { Position=new(0,2.8f,0), Billboard=BaseMaterial3D.BillboardModeEnum.Enabled };
    private double _quickReadyAt;
    private DefenseState _state;
    private DefenseAction _lastAction;
    private Window? _focusWindow;
    public bool IsRecoverAiming => _quickHeld;
    public AbilityAreaVisual RecoveryArea => _quickArea;
    private double _heartbeat, _parryUntil;
    private V2 _facing = V2.UnitY;
    private MeshInstance3D _guard = null!;
    private readonly StandardMaterial3D _material = new() { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency=BaseMaterial3D.TransparencyEnum.Alpha, CullMode=BaseMaterial3D.CullModeEnum.Disabled };
    public void Initialize(PlayerController player, NetworkClient network, PlayerHud hud)
    {
        _player=player; _network=network; _hud=hud;
        _focusWindow=GetWindow(); _focusWindow.FocusExited+=CancelQuick;
        AddChild(_quickArea); player.AddChild(_quickLabel);
        _guard=new MeshInstance3D { Mesh=CombatPresentation.BuildCone(1.1f,MathF.PI/2),MaterialOverride=_material,Visible=false,Position=new(0,-.8f,0) };
        player.AddChild(_guard);
        network.DefenseReceived+=Apply;
        if (network.LatestDefense is { } state) Apply(state);
    }
    private void Apply(DefenseState state)
    {
        if (state.OwnerId!=_player.EntityId) return;
        _hud.Apply(state);
        _state=state; _quickReadyAt=Now()+state.QuickRecoverCooldown;
        _player.SetRecoveryRemaining(state.RecoveryRemaining);
        if (_lastAction==DefenseAction.QuickRecover && state.Sequence==_sequence && state.Outcome!=DefenseOutcome.Accepted)
            _quickLabel.Text=state.Outcome switch { DefenseOutcome.NoStamina=>"Не хватает выносливости", DefenseOutcome.Cooldown=>"Восстановление перезаряжается", _=>"Сейчас нельзя восстановиться" };
        _parryUntil=Now()+state.ParryRemaining;
        _player.SetDefenseMovement(state.MovementMultiplier);
        _facing=state.Direction;
    }
    public override void _Input(InputEvent ev)
    {
        if (_quickHeld && (ev is InputEventKey { Pressed:true, PhysicalKeycode:Key.Escape } ||
            ev is InputEventMouseButton { Pressed:true, ButtonIndex:MouseButton.Right }))
        { CancelQuick(); GetViewport().SetInputAsHandled(); return; }
        if (_quickHeld && ev is InputEventKey { Pressed:false, PhysicalKeycode:Key.Space })
        { CancelQuick(); Send(DefenseAction.QuickRecover); GetViewport().SetInputAsHandled(); return; }
        // Releases must reach us even when the pointer or keyboard focus moved into UI.
        if (ev is InputEventKey { Pressed:false, PhysicalKeycode:Key.Tab } && _held) Release();
    }
    public override void _UnhandledInput(InputEvent ev)
    {
        if (!_player.IsAlive || GameUi.GameplayModalOpen || ev is not InputEventKey { Pressed:true, Echo:false } key) return;
        if (key.PhysicalKeycode==Key.Space && _player.Control is CombatControlPhase.Airborne or CombatControlPhase.KnockedDown)
        { _quickHeld=true; ShowQuick(); GetViewport().SetInputAsHandled(); return; }
        if (key.PhysicalKeycode==Key.Tab) { _held=true; Send(DefenseAction.Block); }
        else if (key.PhysicalKeycode==Key.Shift) Send(DefenseAction.Parry);
        else return;
        _player.DefenseHeld=true;
        GetViewport().SetInputAsHandled();
    }
    private void Send(DefenseAction action)
    {
        _lastAction=action;
        if (_player.TryCursorGround(out var point))
        { var offset=point-_player.PredictedPosition; if (offset.LengthSquared()>.000001f) _facing=V2.Normalize(offset); }
        if (++_sequence==0) ++_sequence;
        _network.SendDefense(new(_sequence,action,_facing)); _heartbeat=Now()+.15;
    }
    private void Release() { _held=false; Send(DefenseAction.Release); }
    public override void _Process(double delta)
    {
        if (_quickHeld)
        {
            if (!_player.IsAlive || !_network.RegionActive || GameUi.GameplayModalOpen ||
                _player.Control is not (CombatControlPhase.Airborne or CombatControlPhase.KnockedDown)) CancelQuick();
            else ShowQuick();
        }
        if (_held && (!Input.IsPhysicalKeyPressed(Key.Tab) || !_player.IsAlive || !GetWindow().HasFocus() || GameUi.GameplayModalOpen)) Release();
        if (_held && !_player.IsDashing && Now()>=_heartbeat) Send(DefenseAction.Block);
        _player.DefenseHeld=!_player.IsDashing && (_held || Now()<_parryUntil);
        _guard.Visible=_player.DefenseHeld && _player.IsAlive;
        _guard.Rotation=new(0,Mathf.Atan2(_facing.X,_facing.Y),0);
        _material.AlbedoColor=Now()<_parryUntil ? new Color(.3f,.8f,1,.55f) : new Color(.8f,.7f,.3f,.3f);
    }
    public override void _ExitTree()
    { CancelQuick(); if (GodotObject.IsInstanceValid(_focusWindow)) _focusWindow!.FocusExited-=CancelQuick; if (_network is not null) _network.DefenseReceived-=Apply; }
    private void CancelQuick() { _quickHeld=false; _quickArea.HideArea(); if (GodotObject.IsInstanceValid(_quickLabel)) _quickLabel.Text=""; }
    private void ShowQuick()
    {
        var origin=new System.Numerics.Vector3(_player.PredictedPosition.X,_player.PredictedFoot.Y-_player.AirOffset,_player.PredictedPosition.Y);
        if (_player.TryCursorGround(out var point))
        { var delta=point-_player.PredictedPosition; if (delta.LengthSquared()>.000001f) _facing=V2.Normalize(delta); }
        var valid=Content.Shared.Navigation.SurfaceDash.TryDestination(_player.Navigation,origin,_facing,_state.QuickRecoverRange,out var end,out var height);
        var profile=new AbilityProfile(ushort.MaxValue,AbilityForm.Dash,_state.QuickRecoverRange,.1f,_state.QuickRecoverSpeed,0,12,0,0,
            Area:new(AbilityAreaShape.Corridor,_state.QuickRecoverRange,_player.Navigation.AgentRadius));
        _quickArea.Show(profile,_player.Navigation,origin,origin,_facing,0,new(end.X,height,end.Y),
            valid && Now()>=_quickReadyAt && _state.Stamina>=_state.QuickRecoverCost);
        _quickLabel.Text="Quick Recover · отпустите Space для переката";
    }
    private static double Now()=>Time.GetTicksMsec()/1000d;
}
