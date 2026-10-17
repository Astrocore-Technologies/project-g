using Content.Shared.Combat;
using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;

namespace ProjectG.Tests.UI;

/// <summary>Real LMB input against the isolated bow/staff fixture; no client hit claims.</summary>
public partial class RangedInputSmoke:Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Size=new(1280,720);GetWindow().GrabFocus();
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();world.AutoConnect=false;AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient");
            InventoryState inventory=default;CombatState state=default;AbilityLoadout loadout=default;
            var attacks=new List<AttackEvent>();var phases=new List<BasicActionState>();var flights=new List<AbilityEffectState>();
            network.InventoryReceived+=value=>inventory=value;network.CombatStateReceived+=value=>{if(value.Kind==CombatEntityKind.Player)state=value;};
            network.AbilityLoadoutReceived+=value=>loadout=value;network.AttackReceived+=value=>attacks.Add(value);
            network.BasicActionReceived+=value=>phases.Add(value);network.AbilityEffectReceived+=value=>{if(value.AbilityId==0)flights.Add(value);};
            network.ConnectToServer();await Wait(()=>inventory.Items?.Any(i=>i.Slot==EquipmentSlot.Weapon)==true&&network.RegionActive,"owned inventory and active region");
            network.SendInventory(new(1,InventoryAction.Equip,inventory.Items.First(i=>i.Slot==EquipmentSlot.Weapon).Handle));
            await Wait(()=>state.BasicDelivery==BasicAttackDelivery.Projectile&&loadout.Abilities is not null&&network.LatestDefense is not null,"equipped ranged profile");
            var staff=OS.GetCmdlineUserArgs().Any(a=>a=="--ranged-basic=Staff");
            if(state.BasicOriginHeight!=1 || state.HalfAngleRadians!=0 || state.BasicThickness!=0 ||
                state.BasicProjectileSpeed!=(staff?12:20) || state.BasicProjectileRadius!=(staff ? .2f : .1f))throw new Exception("Equipped projectile geometry differs from the approved weapon profile.");
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            var start=player.PredictedPosition;var camera=GetViewport().GetCamera3D();
            Vector2 Aim(Vector3 offset)=>camera.UnprojectPosition(player.GlobalPosition+offset);
            var point=Aim(new(0,-1,-4));Input.WarpMouse(point);await Delay(.1);
            var mana=loadout.Mana;var stamina=network.LatestDefense!.Value.Stamina;
            Mouse(true,point);Mouse(false,point);await Wait(()=>attacks.Count==1,"one manual ranged contact");
            var first=attacks[0];
            if(first.Delivery!=BasicAttackDelivery.Projectile || first.TargetId.IsValid || first.DirectionY>=0 ||
                Math.Abs(first.OriginHeight-player.PredictedFoot.Y-1)>.02)throw new Exception("Manual ranged ground shot lost delivery, height, pitch or target-free aiming.");
            await Wait(()=>phases.Any(p=>p.ExecutionId==first.ExecutionId&&p.Phase==CombatActionPhase.Finished),"ranged action completion");
            var preparation=phases.First(p=>p.ExecutionId==first.ExecutionId&&p.Phase==CombatActionPhase.Preparation);
            var total=preparation.PreparationSeconds+preparation.ActiveSeconds+preparation.RecoverySeconds;
            if(Math.Abs(preparation.PreparationSeconds/total-(staff ? .4 : .3))>1e-8 || Math.Abs(preparation.ActiveSeconds/total-.1)>1e-8 ||
                !flights.Any(f=>f.EffectId==first.ExecutionId&&f.Phase==AbilityPhase.Flying))throw new Exception("Ranged phases or the independent confirmed flight were missing.");
            await Delay(state.AttackInterval+.25);if(attacks.Count!=1)throw new Exception("A ranged tap repeated automatically.");
            Mouse(true,point);await Wait(()=>attacks.Count>=2,"continuous ranged shot");
            var turned=Aim(new(4,-1,-1));Input.WarpMouse(turned);Input.ParseInputEvent(new InputEventMouseMotion{Position=turned,GlobalPosition=turned});
            await Wait(()=>attacks.Count>=4,"continuous ranged aim update");Mouse(false,turned);
            await Delay(state.AttackInterval+state.Range/state.BasicProjectileSpeed+.3);var count=attacks.Count;await Delay(state.AttackInterval+.2);
            if(attacks.Count!=count || System.Numerics.Vector2.Dot(first.Direction,attacks[^1].Direction)>.8)
                throw new Exception("Ranged release did not stop repetition or held aim stayed at the first shot.");
            if((player.PredictedPosition-start).Length()>.05 || loadout.Mana!=mana || network.LatestDefense.Value.Stamina!=stamina || network.LatestDefense.Value.InCombatRemaining!=0)
                throw new Exception("An empty ranged basic moved the player, spent a resource or started combat.");
            if(attacks.Any(a=>a.Delivery!=BasicAttackDelivery.Projectile)||attacks.Select(a=>a.ExecutionId).Distinct().Count()!=attacks.Count)
                throw new Exception("Ranged executions lost their independent identities.");
            world.QueueFree();await Delay(.2);
            GD.Print("RANGED_BASIC_INPUT_OK: equipped profile, manual 3D LMB, phases, confirmed flight, continuous aim/release, no approach or resource price.");
            GD.Print("COMBAT_INPUT_OK: ranged basic input.");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private static void Mouse(bool pressed,Vector2 at)=>Input.ParseInputEvent(new InputEventMouseButton
        {ButtonIndex=MouseButton.Left,Pressed=pressed,Position=at,GlobalPosition=at});
    private async Task Delay(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> done,string stage)
    {
        var until=Time.GetTicksMsec()+15000;
        while(!done()&&Time.GetTicksMsec()<until)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(!done())throw new TimeoutException(stage);
    }
}
