using Content.Shared.Network;
using Godot;
using ProjectG.Combat;
using ProjectG.Gameplay;
using ProjectG.Networking;
namespace ProjectG.Tests.UI;

/// <summary>Graphical smoke with real input and an isolated server/profile supplied by CLI.</summary>
public partial class CombatInputSmoke : Node
{
    private int _hits, _attacks;
    private double _attackInterval;
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed; GetWindow().Size=new(1280,720); GetWindow().GrabFocus();
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect=false; AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient");
            network.AttackReceived+=hit=> { _attacks++; if(hit.Damage>0) _hits++; };
            network.CombatStateReceived+=state=> { if(state.Kind==CombatEntityKind.Player) _attackInterval=state.AttackInterval; };
            network.ConnectToServer(); await Wait(()=>network.LatestDefense is not null,"defense snapshot"); await Delay(.5);
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            if (OS.GetCmdlineUserArgs().Contains("--attack-input-only"))
            {
                await VerifyAttackInput(world,network,player);
                world.QueueFree(); await Delay(.2);
                GD.Print("ATTACK_INPUT_OK: ground click, Ctrl directional swing, held chord, NPC bypass, modal guard, target bypass, approach/autoattack, manual cancellation.");
                GetTree().Quit(); return;
            }
            // The authored town starts outside the training target AOI; walk to its approach first.
            if (!player.MoveTo(new(-7,7))) throw new Exception("Training approach is not navigable.");
            await Wait(()=>System.Numerics.Vector2.Distance(player.PredictedPosition,new(-7,7))<.3,"training approach");
            var target=world.GetChildren().OfType<Node3D>().Single(p=>p.Name.ToString().StartsWith("TrainingTarget-"));
            var camera=GetViewport().GetCamera3D();
            Input.WarpMouse(camera.UnprojectPosition(target.GlobalPosition)); await Delay(.1);
            KeyEvent(Key.Shift,true); KeyEvent(Key.Shift,false);
            await Wait(()=>network.LatestDefense is { Stamina:90 },"parry cost");
            KeyEvent(Key.Tab,true); await Wait(()=>network.LatestDefense is { Blocking:true },"Tab guard");
            if (OS.GetCmdlineUserArgs().Contains("--capture"))
            { await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/defense-hud.png"); }
            KeyEvent(Key.Tab,false); await Wait(()=>network.LatestDefense is { Blocking:false },"Tab release");
            await Delay(.4);
            var start=player.PredictedPosition;
            var point=camera.UnprojectPosition(target.GlobalPosition);
            Input.WarpMouse(point); Mouse(MouseButton.Left,true,point); Mouse(MouseButton.Left,false,point);
            await Wait(()=>_hits>=2,"automatic approach and repeated attacks");
            if (System.Numerics.Vector2.Distance(start,player.PredictedPosition)<.2) throw new Exception("Autoattack did not approach target.");
            var destination=player.GlobalPosition+new Vector3(3,-1,0);
            point=camera.UnprojectPosition(destination); Input.WarpMouse(point); Mouse(MouseButton.Right,true,point);
            await Delay(.3);
            // Aim above the avatar: the bottom resource HUD intentionally consumes mouse input.
            var follow=player.GlobalPosition+new Vector3(0,-1,-3);
            var beforeFollow=player.PredictedPosition;
            Input.WarpMouse(camera.UnprojectPosition(follow)); await Delay(.4);
            Mouse(MouseButton.Right,false,camera.UnprojectPosition(follow)); await Delay(.3);
            var hits=_hits; await Delay(1.1); if (_hits!=hits) throw new Exception("RMB did not cancel autoattack.");
            if (beforeFollow.Y-player.PredictedPosition.Y<1) throw new Exception("Held RMB did not follow the changed cursor destination.");
            await Wait(()=>network.LatestDefense is { Stamina:>=99.9 },"stamina regeneration");
            var directions=new[] { System.Numerics.Vector2.UnitX,System.Numerics.Vector2.UnitY,-System.Numerics.Vector2.UnitX,-System.Numerics.Vector2.UnitY };
            var dashDirection=directions.First(d=>network.Navigation!.CanTraverse(player.PredictedPosition,player.PredictedPosition+d*3));
            Input.WarpMouse(camera.UnprojectPosition(player.GlobalPosition+new Vector3(dashDirection.X*3,-1,dashDirection.Y*3))); await Delay(.15);
            KeyEvent(Key.Space,true); KeyEvent(Key.Space,false);
            await Wait(()=>network.LatestDefense is { Stamina:<90 },"dash stamina cost");
            world.QueueFree(); await Delay(.2);
            var restoredWorld=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); restoredWorld.AutoConnect=false; AddChild(restoredWorld);
            var restoredNetwork=restoredWorld.GetNode<NetworkClient>("NetworkClient"); restoredNetwork.ConnectToServer();
            await Wait(()=>restoredNetwork.LatestDefense is not null,"stamina reconnect snapshot");
            if (restoredNetwork.LatestDefense!.Value.Stamina>=95) throw new Exception("Reconnect refilled stamina.");
            restoredWorld.QueueFree(); await Delay(.2);
            GD.Print("COMBAT_INPUT_OK: Shift, Tab press/release, approach, repeated attacks, held RMB tracking/cancellation, regeneration, dash stamina, durable reconnect, cleanup."); GetTree().Quit();
        }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private static void KeyEvent(Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=key,Keycode=key,Pressed=pressed });
    private async Task VerifyAttackInput(WorldController world,NetworkClient network,PlayerController player)
    {
        // River City trainer is outside the spawn AOI; approach before testing NPC input.
        var approach=new System.Numerics.Vector2(15,-8);
        if (!player.MoveTo(approach)) throw new Exception("Training approach is not navigable.");
        await Wait(()=>System.Numerics.Vector2.Distance(player.PredictedPosition,approach)<.3,"city arena approach");
        await Delay(.4);
        await Wait(()=>_attackInterval>0 && network.QuestNpcs.Count>0,"combat and NPC baseline");
        var camera=GetViewport().GetCamera3D();
        var start=player.PredictedPosition;
        var ground=camera.UnprojectPosition(player.GlobalPosition+new Vector3(-5,-1,-3));
        await Click(ground);
        await Delay(_attackInterval+.3);
        if (_attacks!=0 || System.Numerics.Vector2.Distance(start,player.PredictedPosition)>.1)
            throw new Exception("Plain ground click attacked or moved the character.");

        // Holding the chord must remain a single intention, not a repeating attack mode.
        Input.WarpMouse(ground); await Delay(.1);
        KeyEvent(Key.Ctrl,true); Mouse(MouseButton.Left,true,ground,true);
        await Wait(()=>_attacks==1,"Ctrl ground swing");
        await Delay(_attackInterval+.3);
        Mouse(MouseButton.Left,false,ground,true); KeyEvent(Key.Ctrl,false);
        if (_attacks!=1 || _hits!=0) throw new Exception("Held Ctrl + LMB repeated or ground swing hit a target.");

        var npc=network.QuestNpcs.Values.OrderBy(n=>System.Numerics.Vector2.DistanceSquared(n.Position,start)).First();
        var npcPoint=camera.UnprojectPosition(new Vector3(npc.Position.X,1,npc.Position.Y));
        await Click(npcPoint,true);
        await Wait(()=>_attacks==2,"Ctrl NPC swing"); await Delay(_attackInterval+.3);
        if (ProjectG.UI.GameUi.GameplayModalOpen || _attacks!=2) throw new Exception("Ctrl NPC click opened dialogue or repeated.");
        await Click(npcPoint);
        await Wait(()=>ProjectG.UI.GameUi.GameplayModalOpen,"plain NPC dialogue");
        await Click(ground,true); await Delay(.3);
        if (_attacks!=2) throw new Exception("Ctrl attack bypassed a modal window.");
        KeyEvent(Key.Escape,true); KeyEvent(Key.Escape,false);
        await Wait(()=>!ProjectG.UI.GameUi.GameplayModalOpen,"close dialogue");

        // The centre dummy is still outside melee range after talking to the trainer.
        var target=world.GetChildren().OfType<Node3D>().Where(n=>n.Name.ToString().StartsWith("TrainingTarget-"))
            .OrderBy(n=>n.GlobalPosition.DistanceSquaredTo(new Vector3(15,1,-14))).First();
        var point=camera.UnprojectPosition(target.GlobalPosition);
        start=player.PredictedPosition;
        await Click(point,true);
        await Wait(()=>_attacks==3,"Ctrl target swing"); await Delay(_attackInterval+.3);
        if (_attacks!=3 || _hits!=0 || System.Numerics.Vector2.Distance(start,player.PredictedPosition)>.1)
            throw new Exception("Ctrl target click started pursuit or autoattack.");

        await Click(point);
        await Wait(()=>_hits>=2,"plain LMB approach and repeated attacks");
        if (System.Numerics.Vector2.Distance(start,player.PredictedPosition)<.2)
            throw new Exception("Autoattack did not approach the dummy.");
        // Even during cooldown the manual chord cancels the selected autoattack target.
        point=camera.UnprojectPosition(player.GlobalPosition+new Vector3(-5,-1,-3));
        await Click(point,true); await Delay(.3);
        var attacks=_attacks; await Delay(_attackInterval+.3);
        if (_attacks!=attacks) throw new Exception("Ctrl click did not cancel autoattack.");
        await Click(point,true);
        await Wait(()=>_attacks==attacks+1,"manual swing after cancellation");
        await Delay(_attackInterval+.3);
        if (_attacks!=attacks+1) throw new Exception("Manual swing resumed autoattack.");
    }
    private async Task Click(Vector2 point,bool ctrl=false)
    {
        Input.WarpMouse(point); await Delay(.1);
        if(ctrl) KeyEvent(Key.Ctrl,true);
        Mouse(MouseButton.Left,true,point,ctrl); Mouse(MouseButton.Left,false,point,ctrl);
        if(ctrl) KeyEvent(Key.Ctrl,false);
    }
    private static void Mouse(MouseButton button,bool pressed,Vector2 point,bool ctrl=false)=>Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=button,Pressed=pressed,Position=point,GlobalPosition=point,CtrlPressed=ctrl });
    private async Task Delay(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> predicate,string stage)
    { var until=Time.GetTicksMsec()+15000; while(!predicate() && Time.GetTicksMsec()<until) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); if(!predicate()) throw new TimeoutException(stage); }
}
