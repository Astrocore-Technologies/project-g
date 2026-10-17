using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.Gameplay;
namespace ProjectG.Tests.UI;

/// <summary>Real Godot input and UDP, using an isolated server and identity supplied by CLI.</summary>
public partial class CombatInputSmoke : Node
{
    private readonly List<AttackEvent> _attacks=[];
    private double _interval;
    public override async void _Ready()
    {
        try
        {
            GetWindow().Size=new(1280,720); GetWindow().GrabFocus();
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            world.AutoConnect=false; AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient");
            var phasedBasics=OS.GetCmdlineUserArgs().Contains("--phased-basics");
            var basicPhases=new List<BasicActionState>();network.BasicActionReceived+=state=>basicPhases.Add(state);
            AbilityEffectState? casting=null; AbilityResult? cancelled=null; AbilityLoadout loadout=default;
            InventoryState inventory=default;CombatState health=default;CombatFeedback? healing=null;
            ProgressionState progression=default;network.ProgressionReceived+=state=>progression=state;
            AbilityResult? lastResult=null;AbilityEffectState? supportState=null;
            AbilityEffectState? dodgeState=null;
            AttackResult? bufferedBasic=null;
            AttackResult? basicCancellation=null;
            network.AttackResultReceived+=result=>{if(result.Outcome==AttackOutcome.Buffered)bufferedBasic=result;};
            network.AttackResultReceived+=result=>{if(result.Outcome is AttackOutcome.Cancelled or AttackOutcome.CancelDenied)basicCancellation=result;};
            AbilityEffectState? channelState=null;
            AbilityEffectState? chargedState=null,automaticCharge=null;
            AbilityEffectState? heldState=null;
            AbilityEffectState? toggleState=null;
            var chargeActivated=false;
            network.InventoryReceived+=state=>inventory=state;
            network.CombatFeedbackReceived+=value=>{if(value.Kind==CombatFeedbackKind.Healing)healing=value;};
            network.AbilityEffectReceived+=state=> { if(state.Phase==AbilityPhase.Telegraph) casting=state; };
            network.AbilityResultReceived+=result=> { lastResult=result;if(result.Outcome==AbilityOutcome.Cancelled) cancelled=result; };
            network.AbilityEffectReceived+=state=>{if(state.AbilityId==2)supportState=state;};
            network.AbilityEffectReceived+=state=>{if(state.AbilityId==3&&state.Phase==AbilityPhase.Dash&&
                (dodgeState is null || state.RemainingSeconds>dodgeState.Value.RemainingSeconds))dodgeState=state;};
            network.AbilityEffectReceived+=state=>{if(state.AbilityId==600)channelState=state;};
            network.AbilityEffectReceived+=state=>
            {
                if(state.AbilityId==602){chargedState=state;if(state.Phase==AbilityPhase.Impact)chargeActivated=true;}
                if(state.AbilityId==603)automaticCharge=state;
                if(state.AbilityId==604)heldState=state;
                if(state.AbilityId==605)toggleState=state;
            };
            network.AbilityLoadoutReceived+=state=>loadout=state;
            network.AttackReceived+=hit=>_attacks.Add(hit);
            network.CombatStateReceived+=state=> { if(state.Kind==CombatEntityKind.Player){_interval=state.AttackInterval;health=state;} };
            network.ConnectToServer(); await Wait(()=>network.LatestDefense is not null && _interval>0,"initial combat state");
            await Wait(()=>progression.Skills?.Any(s=>s.Id==610 && s.IsPassive && s.Level==1 && s.Slot==0)==true && loadout.Abilities is not null,"known passive arrives without an active slot");
            if(loadout.Abilities.Any(s=>s.Id==610))throw new Exception("Passive consumed an active profile slot.");
            if(loadout.Abilities.Any(s=>s.Form==AbilityForm.Projectile && s.ProjectileLaunchHeight!=1))
                throw new Exception("Projectile launch height did not arrive in the owner loadout.");
            await Delay(.5);
            KeyEvent(Key.Tab,true);
            await Wait(()=>network.LatestDefense is {Outcome:DefenseOutcome.NeedsWeapon,Blocking:false},"unarmed Block rejection");
            await Wait(()=>world.FindChildren("*","Label3D",true,false).OfType<Label3D>().Any(label=>label.Text.Contains("Для блока нужно оружие")),"unarmed Block reason");
            KeyEvent(Key.Tab,false);await Delay(.2);
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            var camera=GetViewport().GetCamera3D(); var start=player.PredictedPosition;
            Vector2 Aim(Vector3 offset)=>camera.UnprojectPosition(player.GlobalPosition+offset);
            var point=Aim(new(0,-1,-3)); Input.WarpMouse(point); await Delay(.1);
            Mouse(true,point); Mouse(false,point);
            await Wait(()=>_attacks.Count==1,"single ground tap"); await Delay(_interval+.25);
            if(phasedBasics)
            {
                var firstBasic=_attacks[0];
                if(firstBasic.DirectionY>=-.2f || Math.Abs(firstBasic.OriginHeight-player.PredictedFoot.Y-1)>.01 ||
                    Math.Abs(firstBasic.HalfAngleRadians-Mathf.Pi/4)>.001 || Math.Abs(firstBasic.Thickness-.3f)>.001 ||
                    health.Body!=new CombatBodyProfile(CombatBodyShape.Capsule,2,.4f))
                    throw new Exception("A ground click lost its manual basic pitch, authored volume or public body profile.");
                await Wait(()=>basicPhases.Any(s=>s.ExecutionId==firstBasic.ExecutionId&&s.Phase==Content.Shared.Combat.CombatActionPhase.Finished),"authoritative basic phase completion");
                var preparation=basicPhases.First(s=>s.ExecutionId==firstBasic.ExecutionId&&s.Phase==Content.Shared.Combat.CombatActionPhase.Preparation);
                var total=preparation.PreparationSeconds+preparation.ActiveSeconds+preparation.RecoverySeconds;
                if(Math.Abs(preparation.PreparationSeconds/total-.2)>1e-8 || Math.Abs(preparation.ActiveSeconds/total-.1)>1e-8 || preparation.ServerTick>=firstBasic.ServerTick)
                    throw new Exception("Basic hit arrived without its separate 20/10/70 preparation.");
                GD.Print("BASIC_PHASES_CONFIRMED: manual 3D pitch, public capsule, authored volume/timings, preparation before contact, one execution identity and finish.");
            }
            if(_attacks.Count!=1) throw new Exception("A tap resumed attacks after release.");
            KeyEvent(Key.Ctrl,true); Mouse(true,point,true);
            await Wait(()=>_attacks.Count==2,"manual chord"); await Delay(2*_interval+.25);
            Mouse(false,point,true); KeyEvent(Key.Ctrl,false);
            if(_attacks.Count!=2) throw new Exception("Manual mode repeated while held.");
            Mouse(true,point); await Wait(()=>_attacks.Count>=4,"continuous server repetition");
            var before=_attacks.Count; var previous=_attacks[^1].Direction;
            point=Aim(new(3,-1,-1)); Input.WarpMouse(point);
            Input.ParseInputEvent(new InputEventMouseMotion {Position=point,GlobalPosition=point,ButtonMask=MouseButtonMask.Left});
            await Wait(()=>_attacks.Count>before && System.Numerics.Vector2.Dot(_attacks[^1].Direction,previous)<.8,"manual aim update");
            Mouse(false,point); await Delay(.3); before=_attacks.Count; await Delay(2*_interval+.25);
            if(_attacks.Count!=before) throw new Exception("Released continuous input kept attacking.");
            if(System.Numerics.Vector2.Distance(start,player.PredictedPosition)>.1)
                throw new Exception("Directional attack automatically moved the character.");
            Mouse(true,point); await Wait(()=>_attacks.Count>before,"new deliberate hold");
            KeyEvent(Key.Shift,true); KeyEvent(Key.Shift,false); await Delay(.4);
            before=_attacks.Count; await Delay(2*_interval+.25); Mouse(false,point);
            if(_attacks.Count!=before) throw new Exception("Parry resumed stale held basic input.");
            if(_attacks.Select(a=>a.ExecutionId).Distinct().Count()!=_attacks.Count)
                throw new Exception("Repeated attacks reused a server execution ID.");
            await Delay(.5);
            var mana=loadout.Mana;
            KeyEvent(Key.Q,true); await Delay(.1); KeyEvent(Key.Q,false);
            await Wait(()=>casting is not null,"server preparation phase");
            if(casting is {Form:AbilityForm.Projectile} preparing && Math.Abs(preparing.OriginHeight-player.PredictedFoot.Y-1)>.01)
                throw new Exception("Confirmed projectile preparation uses the wrong launch height.");
            KeyEvent(Key.Escape,true); KeyEvent(Key.Escape,false);
            await Wait(()=>cancelled?.Sequence==casting?.Sequence,"Esc cancellation acknowledged");
            await Delay(.65);
            if(loadout.Mana<mana || loadout.Abilities.First(p=>p.Id==casting!.Value.AbilityId).ReadyInSeconds>0)
                throw new Exception("Voluntary cast cancellation spent mana or started skill cooldown.");
            var skillId=casting!.Value.AbilityId;
            AbilityProfile Skill()=>loadout.Abilities.First(p=>p.Id==skillId);
            if(Skill().MaxCharges!=3 || Skill().Charges!=3) throw new Exception("Isolated three-charge profile was not loaded.");
            KeyEvent(Key.Q,true); await Delay(.1); KeyEvent(Key.Q,false);
            await Wait(()=>Skill().Charges==2,"first charged activation");
            var recharge=Skill().RechargeInSeconds;
            KeyEvent(Key.Q,true); await Delay(.1); KeyEvent(Key.Q,false);
            await Wait(()=>Skill().Charges==1,"second charge before recovery");
            if(Skill().ReadyInSeconds!=0 || Skill().RechargeInSeconds>=recharge)
                throw new Exception("Spending a remaining charge restarted recovery or blocked the remaining charge.");
            await Wait(()=>world.FindChildren("*","Label",true,false).OfType<Label>().Any(label=>label.Text.Contains("1/3")),"HUD charge count");
            await Wait(()=>Skill().Charges==2,"sequential first recovery");
            await Wait(()=>Skill().Charges==3,"sequential final recovery");
            await Wait(()=>inventory.Items is not null,"inventory preparation for healing");
            var armor=inventory.Items.Single(item=>item.Slot==EquipmentSlot.Armor);
            network.SendInventory(new(1,InventoryAction.Equip,armor.Handle));
            await Wait(()=>inventory.Items.Any(item=>item.Handle==armor.Handle&&item.Equipped)&&health.MaxHealth>health.Health,"equipment raises maximum without healing");
            var beforeHealing=health.Health;point=Aim(new(.5f,-1,0));Input.WarpMouse(point);
            Input.ParseInputEvent(new InputEventMouseMotion{Position=point,GlobalPosition=point});await Delay(.1);
            if(!player.TryCursorSurface(out var supportAim,out var supportHeight))throw new Exception("Support smoke could not pick the surface near the caster.");
            if(System.Numerics.Vector2.Distance(supportAim,player.PredictedPosition)>loadout.Abilities.First(p=>p.Id==2).Radius)
                throw new Exception("Support smoke aimed outside the caster's healing area.");
            KeyEvent(Key.W,true);await Delay(.1);KeyEvent(Key.W,false);
            try{await Wait(()=>healing is not null,"confirmed support area healing");}
            catch(TimeoutException)
            {throw new Exception($"Support failed: foot={player.PredictedFoot}, aim={supportAim}/{supportHeight}, result={lastResult}, effect={supportState}, HP={health.Health}/{health.MaxHealth}, profile={loadout.Abilities.FirstOrDefault(p=>p.Id==2)}");}
            if(healing!.Value.TargetId!=player.EntityId || health.Health!=beforeHealing+healing.Value.Amount)
                throw new Exception("Confirmed support feedback and final HP disagree.");
            await Wait(()=>world.FindChildren("*","Label3D",true,false).OfType<Label3D>().Any(label=>label.Text.Contains('+')&&label.Text.Contains("HP")),"positive healing feedback in world UI");
            await Delay(.4);
            ProjectG.UI.GameUi.Navigate("combat");
            await Wait(()=>world.FindChildren("CombatJournalText","Label",true,false).OfType<Label>().Any(label=>label.Text.Contains("Лечение +")),"private combat journal through live UDP");
            KeyEvent(Key.Escape,true);KeyEvent(Key.Escape,false);await Delay(.1);
            KeyEvent(Key.E,true);await Delay(.1);KeyEvent(Key.E,false);
            await Wait(()=>channelState is {Phase:AbilityPhase.Channeling},"channel starts through input");
            await Wait(()=>loadout.Abilities.Any(p=>p.Id==600&&p.PendingEndCharges==1),"deferred channel charge");
            await Wait(()=>world.FindChildren("*","Label",true,false).OfType<Label>().Any(label=>label.Text.Contains("Поддерживается")),"channel HUD status");
            if(!world.FindChildren("*","PanelContainer",true,false).OfType<PanelContainer>().Any(panel=>panel.TooltipText.Contains("2 MP/с")))
                throw new Exception("Channel resource rate was absent from the HUD.");
            var channelSequence=channelState!.Value.Sequence;
            KeyEvent(Key.Escape,true);KeyEvent(Key.Escape,false);
            await Wait(()=>cancelled?.Sequence==channelSequence,"Esc stops active channel");
            await Wait(()=>loadout.Abilities.Any(p=>p.Id==600&&p.PendingEndCharges==0&&p.ReadyInSeconds>0),"channel cooldown begins after stop");
            var chargeMana=loadout.Mana;
            KeyEvent(Key.A,true);
            await Wait(()=>chargedState is {Phase:AbilityPhase.Charging},"Charged begins on press before release");
            await Delay(.8);
            if(chargeActivated || loadout.Abilities.First(p=>p.Id==602).Charges!=1 || loadout.Mana<chargeMana)
                throw new Exception("Held full charge released or paid before key release.");
            KeyEvent(Key.Escape,true);KeyEvent(Key.Escape,false);KeyEvent(Key.A,false);
            await Wait(()=>cancelled?.Sequence==chargedState?.Sequence,"Charged Esc cancellation");
            if(loadout.Abilities.First(p=>p.Id==602).Charges!=1)throw new Exception("Cancelled charge consumed a charge.");
            chargedState=null;KeyEvent(Key.A,true);await Wait(()=>chargedState is {Phase:AbilityPhase.Charging},"second Charged press");
            KeyEvent(Key.A,false);
            await Wait(()=>chargeActivated&&loadout.Abilities.First(p=>p.Id==602).Charges==0,"Charged release activates and pays once");
            await Delay(.3);KeyEvent(Key.S,true);
            await Wait(()=>automaticCharge is {Phase:AbilityPhase.Charging},"automatic Charged press");
            await Wait(()=>loadout.Abilities.First(p=>p.Id==603).Charges==0,"full charge auto-release while key remains down");
            KeyEvent(Key.S,false);await Delay(.3);
            KeyEvent(Key.D,true);await Wait(()=>heldState is {Phase:AbilityPhase.Channeling},"Hold starts on key press");
            await Delay(.3);if(loadout.Abilities.First(p=>p.Id==604).PendingEndCharges!=1)throw new Exception("Hold did not reserve completion cooldown.");
            var heldSequence=heldState!.Value.Sequence;KeyEvent(Key.D,false);
            await Wait(()=>cancelled?.Sequence==heldSequence && loadout.Abilities.First(p=>p.Id==604).ReadyInSeconds>0,"Hold stops on release and starts cooldown");
            lastResult=null;KeyEvent(Key.F,true);KeyEvent(Key.F,false);KeyEvent(Key.F,true);KeyEvent(Key.F,false);
            await Wait(()=>lastResult?.Outcome==AbilityOutcome.Cancelled && loadout.Abilities.First(p=>p.Id==605).ToggleState==Content.Shared.Combat.ToggleState.Off,"rapid Toggle on/off keeps the final off intention");
            await Wait(()=>loadout.Abilities.First(p=>p.Id==605).ReadyInSeconds==0,"rapid Toggle completion settles its cooldown");
            toggleState=null;KeyEvent(Key.F,true);await Wait(()=>toggleState is {Phase:AbilityPhase.Sustaining},"Toggle starts on press without taking the ordinary lane");
            KeyEvent(Key.F,false);
            await Wait(()=>loadout.Abilities.First(p=>p.Id==605).ToggleState==Content.Shared.Combat.ToggleState.Active,"Toggle owner state");
            await Wait(()=>world.FindChildren("*","Label",true,false).OfType<Label>().Any(label=>label.Text=="Включён"),"Toggle HUD state");
            var attacksDuringToggle=_attacks.Count;point=Aim(new(2,-1,0));Input.WarpMouse(point);await Delay(.1);Mouse(true,point);Mouse(false,point);
            await Wait(()=>_attacks.Count>attacksDuringToggle,"basic attack while background Toggle remains active");
            if(loadout.Abilities.First(p=>p.Id==605).ToggleState!=Content.Shared.Combat.ToggleState.Active)throw new Exception("Basic attack unexpectedly disabled a background Toggle.");
            casting=null;KeyEvent(Key.Q,true);await Delay(.1);KeyEvent(Key.Q,false);
            await Wait(()=>casting is not null,"ordinary Cast while background Toggle remains active");
            KeyEvent(Key.Escape,true);KeyEvent(Key.Escape,false);
            await Wait(()=>cancelled?.Sequence==casting?.Sequence,"Esc cancels ordinary Cast rather than background Toggle");
            if(loadout.Abilities.First(p=>p.Id==605).ToggleState!=Content.Shared.Combat.ToggleState.Active)throw new Exception("Esc on the ordinary Cast also stopped background Toggle.");
            KeyEvent(Key.F,true);KeyEvent(Key.F,false);
            await Wait(()=>loadout.Abilities.First(p=>p.Id==605).ToggleState==Content.Shared.Combat.ToggleState.Off && loadout.Abilities.First(p=>p.Id==605).ReadyInSeconds>0,"second Toggle press stops only its own sustain");
            await Wait(()=>loadout.Abilities.First(p=>p.Id==1).Charges>0,"a charge for the buffered basic scenario");
            casting=null;bufferedBasic=null;var bufferedAttackCount=_attacks.Count;
            KeyEvent(Key.Q,true);KeyEvent(Key.Q,false);await Wait(()=>casting is not null,"Cast before an early manual basic");
            // Aim inside the 150 ms buffer using the remaining authoritative cast,
            // including time already spent before the first publication at this TPS.
            await Delay(Math.Max(0,casting!.Value.RemainingSeconds-.12));Mouse(true,point,true);Mouse(false,point,true);
            await Wait(()=>bufferedBasic is not null,"manual basic enters the common buffer during an uncancellable Cast phase");
            await Wait(()=>_attacks.Count==bufferedAttackCount+1,"the buffered basic executes at the legal boundary");
            await Delay(_interval+.2);
            if(_attacks.Count!=bufferedAttackCount+1)throw new Exception("The buffered manual basic repeated after its one execution.");
            await Wait(()=>loadout.Abilities.First(p=>p.Id==1).Charges>0,"a charge for replacing a buffered skill");
            casting=null;lastResult=null;KeyEvent(Key.Q,true);KeyEvent(Key.Q,false);
            await Wait(()=>casting is not null,"the Cast that forbids skill cancellation");
            var protectedCast=casting!.Value.Sequence;var oldChannel=channelState;
            KeyEvent(Key.E,true);KeyEvent(Key.E,false);
            await Wait(()=>lastResult?.Outcome==AbilityOutcome.Buffered,"first skill waits without cancelling the Cast");
            var replacedSequence=lastResult!.Value.Sequence;
            KeyEvent(Key.W,true);KeyEvent(Key.W,false);
            await Wait(()=>lastResult?.Outcome==AbilityOutcome.Buffered&&lastResult?.Sequence!=replacedSequence,"another key replaces the single buffered skill");
            var replacementSequence=lastResult!.Value.Sequence;
            await Wait(()=>lastResult?.Sequence==replacementSequence&&lastResult?.Outcome==AbilityOutcome.Busy,"the replacement expires rather than cancelling the protected Cast");
            if(channelState!=oldChannel || casting?.Sequence!=protectedCast)
                throw new Exception("A replaced or expired buffered skill executed, or cancelled the protected Cast.");
            await Delay(.35);
            // Reconnect fixture: a fresh presentation must advance beyond the retained body's acknowledged inputs.
            player.GetChildren().OfType<ProjectG.Combat.AbilityPresentation>().Single().ApplyLoadout(loadout with {LastInputSequence=5000});
            casting=null;cancelled=null;KeyEvent(Key.Q,true);await Delay(.1);KeyEvent(Key.Q,false);
            await Wait(()=>casting is {Sequence:5001},"server input floor seeds the next real key command");
            KeyEvent(Key.Escape,true);KeyEvent(Key.Escape,false);
            await Wait(()=>cancelled?.Sequence==5001,"Esc addresses the confirmed server execution");
            KeyEvent(Key.R,true);await Delay(.1);KeyEvent(Key.R,false);
            await Wait(()=>lastResult?.Outcome==AbilityOutcome.NoMana,"channel resource exhaustion result");
            await Wait(()=>world.FindChildren("*","Label3D",true,false).OfType<Label3D>().Any(label=>label.Text.Contains("Поддержание прекращено: закончилась мана")),"channel resource exhaustion reason");
            await Delay(.25);
            var dodge=loadout.Abilities.Single(p=>p.Form==AbilityForm.Dash);
            if(dodge.DodgeDurationSeconds!=.2 || dodge.DodgeProtectionSeconds!=.15 || dodge.StaminaCost!=20)
                throw new Exception("The approved Dodge profile did not arrive in the owner HUD.");
            var dodgeStamina=network.LatestDefense!.Value.Stamina;
            point=Aim(new(1,-1,0));Input.WarpMouse(point);await Delay(.05);
            KeyEvent(Key.Space,true);await Delay(.05);KeyEvent(Key.Space,false);
            // Receipt admission can precede publication by part of the negotiated world tick.
            var minimumPublishedDodge=.2-1d/network.ServerTickRate-.001;
            await Wait(()=>dodgeState is {} state&&state.RemainingSeconds>=minimumPublishedDodge,"real Space input starts the approved Dodge");
            if(dodgeState!.Value.RemainingSeconds<minimumPublishedDodge || dodgeState.Value.RemainingSeconds>.201f)
                throw new Exception("A short cursor path changed the published Dodge duration.");
            await Wait(()=>Math.Abs(network.LatestDefense!.Value.Stamina-(dodgeStamina-20))<.5,"Dodge pays its Stamina exactly once");
            if(world.FindChildren("*","Label3D",true,false).OfType<Label3D>().Any(label=>label.Text.Contains("УВОРОТ")))
                throw new Exception("Starting Dodge invented a confirmed avoided hit.");
            GD.Print("DODGE_CONFIRMED: Space input, owner profile, fixed short-path duration, authoritative cost and no predicted defense success.");
            await Delay(.3);
            if(phasedBasics)
            {
                await Delay(_interval+.2);var attackCount=_attacks.Count;
                var combat=player.GetChildren().OfType<ProjectG.Combat.CombatPresentation>().Single();
                combat.ApplyBasicInput(new(health.EntityId,health.ServerTick,5000,7000));
                point=Aim(new(0,-1,-3));Input.WarpMouse(point);await Delay(.05);
                Mouse(true,point,true);Mouse(false,point,true);
                await Wait(()=>basicPhases.Any(s=>s.Sequence==5001&&s.Phase==Content.Shared.Combat.CombatActionPhase.Preparation),"basic floor seeds the next click");
                KeyEvent(Key.Escape,true);KeyEvent(Key.Escape,false);
                await Wait(()=>basicCancellation is {Sequence:5001,Outcome:AttackOutcome.Cancelled},"Esc cancels the owned basic preparation");
                await Delay(_interval+.2);if(_attacks.Count!=attackCount)throw new Exception("Cancelled basic preparation produced a contact.");
                if(!player.TryCursorCombatPoint(out var clicked,out var clickedY,point))throw new Exception("Missing original manual aim point.");
                var clickOrigin=player.PredictedFoot+System.Numerics.Vector3.UnitY*health.BasicOriginHeight;
                var clickDirection=System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(clicked.X,clickedY,clicked.Y)-clickOrigin);
                Mouse(true,point,true);Mouse(false,point,true);await Delay(.02);
                var turnPoint=Aim(new(3,-1,0));Input.WarpMouse(turnPoint);
                Input.ParseInputEvent(new InputEventMouseMotion {Position=turnPoint,GlobalPosition=turnPoint});
                await Wait(()=>_attacks.Any(a=>a.Sequence==5002),"manual basic contact after preparation aim update");
                var turned=_attacks.First(a=>a.Sequence==5002);
                var confirmedDirection=new System.Numerics.Vector3(turned.Direction.X,turned.DirectionY,turned.Direction.Y);
                // The first published preparation pose may already include the turn.
                // Compare to the actual click, not to that coalesced network observation.
                if(System.Numerics.Vector3.Dot(clickDirection,confirmedDirection)>.8)
                    throw new Exception($"Manual basic direction stayed at the click despite a preparation turn: click={clickDirection}, contact={confirmedDirection}.");
                await Wait(()=>basicPhases.Any(s=>s.Sequence==5002&&s.Phase==Content.Shared.Combat.CombatActionPhase.Recovery),"basic recovery");
                KeyEvent(Key.Escape,true);KeyEvent(Key.Escape,false);
                await Wait(()=>basicCancellation is {Sequence:5002,Outcome:AttackOutcome.Cancelled},"Esc cancels owned basic recovery");
                GD.Print("BASIC_CONTROL_CONFIRMED: owner floor 5000 -> click 5001, preparation/recovery Esc and manual preparation aim.");
            }
            if(network.LatestDefense!.Value.InCombatRemaining!=0)throw new Exception("Empty attacks incorrectly started combat.");
            var navigation=world.GetChildren().OfType<ProjectG.UI.GameNavigation>().Single();navigation.ToggleMenu();await Delay(.1);
            var logout=world.FindChildren("LogoutButton","Button",true,false).OfType<Button>().Single();logout.EmitSignal(Button.SignalName.Pressed);
            await Wait(()=>network.LoggedOut,"menu logout committed and acknowledged");
            if(network.LastConnectionError.Length!=0)throw new Exception("Ordinary logout was presented as a connection error.");
            world.QueueFree(); await Delay(.2);
            GD.Print("COMBAT_INPUT_OK: manual/held/release/aim, early manual basic buffered during Cast and executed once, buffered skill replacement and expiry without cancelling a protected Cast, no approach, Parry, execution IDs, Cast/Esc, sequential charges/HUD, support area, private journal, Channel/Esc/rates/exhaustion, Charged, Hold, rapid and ordinary background Toggle, learned Passive without active slot via UDP/UI, empty actions stay out of combat, menu logout and cleanup.");
            GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static void KeyEvent(Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=key,Keycode=key,Pressed=pressed });
    private static void Mouse(bool pressed,Vector2 point,bool ctrl=false)=>Input.ParseInputEvent(new InputEventMouseButton
        { ButtonIndex=MouseButton.Left,Pressed=pressed,Position=point,GlobalPosition=point,CtrlPressed=ctrl });
    private async Task Delay(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> predicate,string stage)
    {
        var until=Time.GetTicksMsec()+15000;
        while(!predicate() && Time.GetTicksMsec()<until) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(!predicate()) throw new TimeoutException(stage);
    }
}
