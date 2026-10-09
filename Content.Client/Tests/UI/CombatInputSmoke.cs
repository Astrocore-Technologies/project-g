using Content.Shared.Network;
using Godot;
using ProjectG.Combat;
using ProjectG.Gameplay;
using ProjectG.Networking;
namespace ProjectG.Tests.UI;

/// <summary>Graphical smoke with real input and an isolated server/profile supplied by CLI.</summary>
public partial class CombatInputSmoke : Node
{
    private int _hits;
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed; GetWindow().Size=new(1280,720); GetWindow().GrabFocus();
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect=false; AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient");
            network.AttackReceived+=hit=> { if(hit.Damage>0) _hits++; };
            network.ConnectToServer(); await Wait(()=>network.LatestDefense is not null,"defense snapshot"); await Delay(.5);
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
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
    private static void Mouse(MouseButton button,bool pressed,Vector2 point)=>Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=button,Pressed=pressed,Position=point,GlobalPosition=point });
    private async Task Delay(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> predicate,string stage)
    { var until=Time.GetTicksMsec()+15000; while(!predicate() && Time.GetTicksMsec()<until) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); if(!predicate()) throw new TimeoutException(stage); }
}
