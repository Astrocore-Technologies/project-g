using Content.Shared.Combat;
using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Items;
using ProjectG.Networking;
using ProjectG.UI;

namespace ProjectG.Tests.UI;

/// <summary>Real inventory buttons, LMB and Tab against an isolated authoritative server.</summary>
public partial class WeaponEquipmentSmoke:Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Size=new(1280,720);GetWindow().GrabFocus();
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();world.AutoConnect=false;AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient");InventoryState state=default;CombatState combat=default;
            var results=new List<InventoryResult>();var phases=new List<BasicActionState>();
            network.InventoryReceived+=value=>state=value;network.InventoryResultReceived+=value=>results.Add(value);
            network.CombatStateReceived+=value=>{if(value.Kind==CombatEntityKind.Player)combat=value;};network.BasicActionReceived+=value=>phases.Add(value);
            network.ConnectToServer();await Wait(()=>network.RegionActive&&state.Items is {Count:5}&&network.LatestDefense is not null,"owner equipment baseline");
            var inventory=world.GetChildren().OfType<InventoryPresentation>().Single();
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            async Task Change(string name,bool equipped,bool incompatible=false)
            {
                var icon=inventory.FindChildren("*","Button",true,false).OfType<UiIconSlot>().Single(b=>!b.Disabled&&b.TooltipText==name);
                await Click(icon);var count=results.Count;
                var action=inventory.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text is "Надеть" or "Снять");
                await Click(action);await Wait(()=>results.Count>count,"equipment acknowledgement");
                if(results[^1].Outcome!=(incompatible?InventoryOutcome.IncompatibleEquipment:InventoryOutcome.Accepted))throw new Exception("Unexpected equipment result.");
                await Wait(()=>state.Items.Single(i=>i.Name==name).Equipped==equipped,"committed equipment state");
            }
            async Task Basic(double fraction,float range)
            {
                var count=phases.Count;var point=GetViewport().GetCamera3D().UnprojectPosition(player.GlobalPosition+new Vector3(0,-1,-4));
                Input.WarpMouse(point);await Delay(.1);Mouse(true,point);Mouse(false,point);
                await Wait(()=>phases.Skip(count).Any(p=>p.Phase==CombatActionPhase.Preparation),"manual weapon preparation");
                var preparation=phases.Skip(count).First(p=>p.Phase==CombatActionPhase.Preparation);
                var total=preparation.PreparationSeconds+preparation.ActiveSeconds+preparation.RecoverySeconds;
                if(Math.Abs(preparation.PreparationSeconds/total-fraction)>1e-8 || preparation.Range!=range || preparation.Delivery!=BasicAttackDelivery.MeleeArc)
                    throw new Exception("Equipped weapon lost its approved basic profile.");
                await Wait(()=>phases.Any(p=>p.ExecutionId==preparation.ExecutionId&&p.Phase==CombatActionPhase.Finished),"weapon recovery completion");
            }
            inventory.Toggle();await Delay(.15);await Change("Учебное двуручное",true);KeyEvent(Godot.Key.I);await Delay(.15);
            await Wait(()=>combat.Range==3,"two-handed combat profile");await Basic(.4,3);
            inventory.Toggle();await Delay(.15);await Change("Учебный щит",false,true);
            if(!inventory.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains("несовместим")))throw new Exception("Shield incompatibility had no readable reason.");
            await Change("Учебное двуручное",false);await Change("Учебный щит",true);
            if(!inventory.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains("Поглощение Block +20")))throw new Exception("Shield absorption bonus is missing from inventory.");
            KeyEvent(Godot.Key.I);await Delay(.15);await Wait(()=>Math.Abs(network.LatestDefense!.Value.BlockDamage-.5)<1e-9,"shield absorption");
            Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=Godot.Key.Tab,Keycode=Godot.Key.Tab,Pressed=true});
            await Wait(()=>network.LatestDefense is {Blocking:true},"shield-only Tab Block");
            Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=Godot.Key.Tab,Keycode=Godot.Key.Tab,Pressed=false});
            await Wait(()=>network.LatestDefense is {Blocking:false},"Tab release");
            inventory.Toggle();await Delay(.15);await Change("Учебные кинжалы",true);KeyEvent(Godot.Key.I);await Delay(.15);
            await Wait(()=>combat.Range==1.5,"dagger combat profile");await Basic(.2,1.5f);
            if(!state.Items.Any(i=>i.Slot==EquipmentSlot.Offhand&&i.Equipped)||network.LatestDefense!.Value.InCombatRemaining!=0)
                throw new Exception("Compatible daggers displaced the shield or an empty swing started combat.");
            world.QueueFree();await Delay(.2);if(GameUi.GameplayModalOpen)throw new Exception("Equipment UI leaked its input lock.");
            GD.Print("WEAPON_EQUIPMENT_OK: inventory clicks, two-handed/dagger basic, shield incompatibility, +20 absorption, shield-only Tab Block and cleanup.");
            GD.Print("COMBAT_INPUT_OK: weapon equipment.");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private static void KeyEvent(Godot.Key key)
    {
        Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=key,Keycode=key,Pressed=true});
        Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=key,Keycode=key,Pressed=false});
    }
    private static void Mouse(bool pressed,Vector2 at)=>Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=pressed,Position=at,GlobalPosition=at});
    private async Task Click(Button button)
    {var at=button.GetGlobalRect().GetCenter();Input.WarpMouse(at);await Delay(.05);Mouse(true,at);await Delay(.05);Mouse(false,at);await Delay(.05);}
    private async Task Delay(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> done,string stage)
    {var end=Time.GetTicksMsec()+15000;while(!done()&&Time.GetTicksMsec()<end)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(!done())throw new TimeoutException(stage);}
}
