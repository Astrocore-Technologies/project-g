using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Shared.Navigation;
using Godot;
using ProjectG.Combat;
using ProjectG.Gameplay;
using ProjectG.UI;
using V2=System.Numerics.Vector2;
using V3=System.Numerics.Vector3;

namespace ProjectG.Echoes;

public partial class EchoControls
{
    private readonly Dictionary<NetworkEntityId,EchoPresentation> _signatureBodies=new();
    private readonly Dictionary<NetworkEntityId,AbilityProfile> _signatureProfiles=new();
    private readonly Dictionary<NetworkEntityId,AbilityEffectState> _signatureExecutions=new();
    private readonly Dictionary<NetworkEntityId,uint> _heldSignatures=new();
    private readonly Dictionary<NetworkEntityId,(V2 Aim,float Height,float Y)> _lastSignatureAim=new();
    private AbilityAreaVisual _signatureArea=null!;
    private PlayerController? _signaturePlayer;
    private Window? _signatureWindow;
    private byte _signatureAimSlot;
    private string? _signatureAimAction;
    private uint _signatureControlSequence;
    private readonly uint[] _issuedSignatureBySlot=new uint[NetworkConstants.MaxActiveEchoes];
    private readonly bool[] _pendingSignatureSlots=new bool[NetworkConstants.MaxActiveEchoes];
    private readonly double[] _signatureSentAt=new double[NetworkConstants.MaxActiveEchoes];
    private double _signatureRefresh;

    private void InitializeSignatureInput()
    {
        _signatureArea=new();GetParent().AddChild(_signatureArea);
        _signatureWindow=GetWindow();_signatureWindow.FocusExited+=CancelSignatureInput;
    }
    private void DisposeSignatureInput()
    {
        CancelSignatureInput();
        if(GodotObject.IsInstanceValid(_signatureWindow))_signatureWindow!.FocusExited-=CancelSignatureInput;
        if(GodotObject.IsInstanceValid(_signatureArea))_signatureArea.QueueFree();
    }
    private bool TrySignatureSlot(byte slot,out EchoSlot value)
    {
        if(_loadout.Slots is not null)foreach(var candidate in _loadout.Slots)if(candidate.Slot==slot){value=candidate;return true;}
        value=default;return false;
    }
    public void ApplySignatureProfile(AbilityLoadout loadout)
    {
        if(_loadout.Slots is null||!_loadout.Slots.Any(s=>s.EntityId==loadout.EntityId)||loadout.Abilities.Count!=1)return;
        _signatureProfiles[loadout.EntityId]=loadout.Abilities[0];
        foreach(var slot in _loadout.Slots)if(slot.EntityId==loadout.EntityId)_cooldowns[slot.Slot-1]=loadout.Abilities[0].ReadyInSeconds;
    }
    public void ConfirmSignature(AbilityEffectState state)
    {
        if(!_signatureProfiles.TryGetValue(state.ActorId,out var profile)||profile.Id!=state.AbilityId)return;
        if(state.Phase==AbilityPhase.Finished)
        {
            if(_signatureExecutions.TryGetValue(state.ActorId,out var previous)&&previous.EffectId==state.EffectId)
            {_signatureExecutions.Remove(state.ActorId);_heldSignatures.Remove(state.ActorId);}
            return;
        }
        if(state.Phase is AbilityPhase.Flying or AbilityPhase.Impact or AbilityPhase.Recovery)return;
        if(!_signatureExecutions.TryGetValue(state.ActorId,out var current)||state.EffectId>=current.EffectId)
            _signatureExecutions[state.ActorId]=state;
    }
    private void ForgetSignature(NetworkEntityId id)
    {_signatureBodies.Remove(id);_signatureProfiles.Remove(id);_signatureExecutions.Remove(id);_heldSignatures.Remove(id);_lastSignatureAim.Remove(id);}
    private void PruneSignatures()
    {
        foreach(var id in _signatureProfiles.Keys.ToArray())
            if(_loadout.Slots is null||!_loadout.Slots.Any(s=>s.EntityId==id))ForgetSignature(id);
    }
    private void BeginSignatureAim(byte slot,string action)
    {
        if(!TrySignatureSlot(slot,out var echo)||!_signatureProfiles.TryGetValue(echo.EntityId,out var profile))
        {_status.Text="Сведения о способности загружаются…";return;}
        if(_pendingSignatureSlots[slot-1]||!_alive||!_network.RegionActive)return;
        _signatureAimSlot=slot;_signatureAimAction=action;
        if(profile.ExecutionForm==CombatActionForm.Toggle){CommitSignature(slot);ClearSignatureAim();return;}
        if(profile.ExecutionForm is CombatActionForm.Charged or CombatActionForm.Hold)
        {
            if(CommitSignature(slot))_heldSignatures[echo.EntityId]=_sequence;
            else ClearSignatureAim();
        }
        RefreshSignatureAim();
    }
    public override void _Input(InputEvent input)
    {
        if(PcInput.Pressed(PcInput.Cancel,input) || PcInput.Pressed(PcInput.Move,input))
        {
            if(_signatureAimSlot!=0||_heldSignatures.Count>0){CancelSignatureInput();GetViewport().SetInputAsHandled();}
            else if(PcInput.Pressed(PcInput.Cancel,input) && !GameUi.GameplayModalOpen && TrySignatureSlot(_selected,out var selected) && _signatureExecutions.ContainsKey(selected.EntityId))
            {SendSignatureControl(selected.EntityId,EchoSignatureControlKind.Cancel);GetViewport().SetInputAsHandled();}
            return;
        }
        byte slot=0;
        for(var i=0;i<PcInput.Echoes.Length;i++)if(PcInput.Released(PcInput.Echoes[i],input)){slot=(byte)(i+1);break;}
        if(slot==0)return;
        if(TrySignatureSlot(slot,out var echo)&&_heldSignatures.ContainsKey(echo.EntityId))
        {
            SendSignatureControl(echo.EntityId,EchoSignatureControlKind.Release);_heldSignatures.Remove(echo.EntityId);
            if(_signatureAimSlot==slot)ClearSignatureAim();GetViewport().SetInputAsHandled();return;
        }
        if(_signatureAimSlot==slot&&_signatureAimAction is not null&&PcInput.Released(_signatureAimAction,input))
        {CommitSignature(slot);ClearSignatureAim();GetViewport().SetInputAsHandled();}
    }
    private bool TrySignatureAim(NetworkEntityId id,AbilityProfile profile,out V3 foot,out V2 aim,out float height,
        out V2 direction,out float dy,out float distance,out V3 dashEnd)
    {
        foot=dashEnd=default;aim=default;height=dy=distance=0;direction=V2.UnitY;
        if(!GodotObject.IsInstanceValid(_signaturePlayer))
            _signaturePlayer=GetParent().GetChildren().OfType<PlayerController>().FirstOrDefault(p=>p.EntityId==_owner);
        if(_signaturePlayer is null||!_signatureBodies.TryGetValue(id,out var body)||!GodotObject.IsInstanceValid(body))return false;
        var position=body.GlobalPosition;foot=new(position.X,position.Y-1,position.Z);dashEnd=foot;
        if(profile.Area.Shape==AbilityAreaShape.Self){aim=new(foot.X,foot.Z);height=foot.Y;return true;}
        if(!(profile.Form is AbilityForm.Projectile or AbilityForm.Melee?_signaturePlayer.TryCursorCombatPoint(out aim,out height):
            _signaturePlayer.TryCursorSurface(out aim,out height)))return false;
        var offset=aim-new V2(foot.X,foot.Z);
        direction=offset.LengthSquared()>.000001f?V2.Normalize(offset):V2.UnitY;
        if(profile.Form is AbilityForm.Projectile or AbilityForm.Melee)
        {
            var vertical=height-foot.Y-profile.AttackOriginHeight;var length=MathF.Sqrt(offset.LengthSquared()+vertical*vertical);
            if(length<.0001f)return false;direction=offset/length;dy=vertical/length;
        }
        if(profile.Form==AbilityForm.Dash)
        {
            distance=Math.Min(profile.Range,offset.Length());
            if(distance<.001f||!SurfaceDash.TryDestination(_signaturePlayer.Navigation,foot,direction,distance,out var end,out var endHeight))return false;
            dashEnd=new(end.X,endHeight,end.Y);
        }
        return true;
    }
    private bool CommitSignature(byte slot)
    {
        if(GameUi.GameplayModalOpen||!_alive||!_network.RegionActive||_pendingSignatureSlots[slot-1]||!TrySignatureSlot(slot,out var echo)||
            !_signatureProfiles.TryGetValue(echo.EntityId,out var profile))return false;
        var off=profile.ExecutionForm==CombatActionForm.Toggle&&profile.ToggleState!=ToggleState.Off;
        if(!off && (_refused[slot-1]||_cooldowns[slot-1]>0))
        {_status.Text=_refused[slot-1]?"Эхо сейчас отказывается применять эту способность":"Способность ещё восстанавливается";return false;}
        V2 aim=V2.UnitY,direction=V2.UnitY;float height=0,dy=0,distance=0;
        if(!off&&!TrySignatureAim(echo.EntityId,profile,out _,out aim,out height,out direction,out dy,out distance,out _))return false;
        if(++_sequence==0)++_sequence;_pending=true;_sentAt=Time.GetTicksMsec()/1000d;
        _issuedSignatureBySlot[slot-1]=_sequence;
        _pendingSignatureSlots[slot-1]=true;_signatureSentAt[slot-1]=_sentAt;
        _lastSignatureAim[echo.EntityId]=(profile.Form==AbilityForm.GroundArea?aim:direction,height,dy);
        _network.SendEchoSignature(new(_sequence,_network.LatestServerTick,slot,profile.Form==AbilityForm.GroundArea?aim:direction,height,dy,distance,
            off?AbilityIntent.Deactivate:AbilityIntent.Use,echo.EntityId));return true;
    }
    private void SendSignatureControl(NetworkEntityId id,EchoSignatureControlKind kind)
    {
        uint sequence;ulong execution=0;
        if(_signatureExecutions.TryGetValue(id,out var active)){sequence=active.Sequence;execution=active.EffectId;}
        else if(!_heldSignatures.TryGetValue(id,out sequence))return;
        var saved=_lastSignatureAim.GetValueOrDefault(id,(V2.UnitY,0,0));
        var aim=saved.Item1;float height=saved.Item2,dy=saved.Item3;
        if(kind!=EchoSignatureControlKind.Cancel && _signatureProfiles.TryGetValue(id,out var profile) &&
            TrySignatureAim(id,profile,out _,out var point,out height,out var direction,out dy,out _,out _))
        {aim=profile.Form==AbilityForm.GroundArea?point:direction;_lastSignatureAim[id]=(aim,height,dy);}
        if(++_signatureControlSequence==0)++_signatureControlSequence;
        _network.SendEchoControl(new(_signatureControlSequence,id,sequence,execution,_network.LatestServerTick,kind,aim,height,dy));
    }
    private void ClearSignatureAim(){_signatureAimSlot=0;_signatureAimAction=null;_signatureArea?.HideArea();}
    private void CancelSignatureInput()
    {
        foreach(var id in _heldSignatures.Keys)SendSignatureControl(id,EchoSignatureControlKind.Cancel);
        _heldSignatures.Clear();ClearSignatureAim();
    }
    private void SignatureResult(EchoSignatureResult value)
    {
        if(value.Outcome==EchoCommandOutcome.Accepted)return;
        foreach(var id in _heldSignatures.Where(p=>p.Value==value.Sequence).Select(p=>p.Key).ToArray())_heldSignatures.Remove(id);
    }
    private void UpdateSignatureInput(double delta)
    {
        var now=Time.GetTicksMsec()/1000d;
        for(var i=0;i<_pendingSignatureSlots.Length;i++)if(_pendingSignatureSlots[i]&&now-_signatureSentAt[i]>10)
        {_pendingSignatureSlots[i]=false;_status.Text="Нет ответа. Попробуйте снова.";}
        _pending=_pendingSignatureSlots.Any(p=>p);
        if(!_alive||!_network.RegionActive||GameUi.GameplayModalOpen){CancelSignatureInput();return;}
        _signatureRefresh+=delta;if(_signatureRefresh<.05)return;_signatureRefresh=0;
        if(_signatureAimSlot!=0)RefreshSignatureAim();
        foreach(var id in _heldSignatures.Keys)SendSignatureControl(id,EchoSignatureControlKind.Aim);
    }
    private void RefreshSignatureAim()
    {
        if(!TrySignatureSlot(_signatureAimSlot,out var echo)||!_signatureProfiles.TryGetValue(echo.EntityId,out var profile)||
            !TrySignatureAim(echo.EntityId,profile,out var foot,out var aim,out var height,out var direction,out var dy,out _,out var dashEnd))
        {_signatureArea.HideArea();return;}
        var target=new V3(aim.X,height,aim.Y);var grid=_signaturePlayer!.Navigation;
        var allowed=!_refused[echo.Slot-1]&&_cooldowns[echo.Slot-1]<=0 && (profile.Form!=AbilityForm.GroundArea||
            V3.DistanceSquared(foot,target)<=profile.Range*profile.Range&&grid.ClearAttack(foot,target));
        _signatureArea.Show(profile,grid,foot,target,direction,dy,dashEnd,allowed);
    }
}
