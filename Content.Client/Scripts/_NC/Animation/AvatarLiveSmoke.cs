using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using V2=System.Numerics.Vector2;
namespace ProjectG.Animation;

/// <summary>Two real UDP clients exercise rendered local/remote poses against an isolated server.</summary>
public partial class AvatarLiveSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var tag=Guid.NewGuid().ToString("N")[..10];
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();world.AutoConnect=false;
            var owner=world.GetNode<NetworkClient>("NetworkClient");owner.DevelopmentIdentityProfile="anim-owner-"+tag;
            AddChild(world);owner.ConnectToServer();
            PlayerController? local=null;
            await Until(()=> {local=world.GetChildren().OfType<PlayerController>().FirstOrDefault(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);return local is not null;},"Owner spawn");
            var observer=new NetworkClient {DevelopmentIdentityProfile="anim-peer-"+tag,SimulateNetworkConditions=true,SimulatedMinLatencyMs=60,SimulatedMaxLatencyMs=90,SimulatedPacketLossPercent=3};
            AddChild(observer);observer.ConnectToServer();
            PlayerController? remote=null;
            await Until(()=> {remote=world.GetChildren().OfType<PlayerController>().FirstOrDefault(p=>p.EntityId!=local!.EntityId);return remote is not null;},"Observer spawn");
            AvatarState observed=default;observer.AvatarReceived+=s=> {if(s.EntityId==local!.EntityId)observed=s;};
            owner.SendEmote(new(1,AvatarGesture.Wave));
            await Until(()=>local!.Animator.CurrentClip=="wave" && observed.Gesture==AvatarGesture.Wave,"Local emote observed over UDP");
            observer.SendEmote(new(1,AvatarGesture.Cheer));
            await Until(()=>remote!.Animator.CurrentClip=="cheer","Remote avatar animation");
            owner.SendEmote(new(2,AvatarGesture.None));observer.SendEmote(new(2,AvatarGesture.None));
            var point=local!.PredictedPosition+new V2(2,0);local.MoveTo(point);
            await Until(()=>local.Animator.CurrentClip.StartsWith("run") || local.Animator.CurrentClip.StartsWith("walk"),"Locomotion from real movement");
            await Until(()=>V2.Distance(local.PredictedPosition,point)<.15,"Movement complete");local.StopMovement();
            owner.SendDefense(new(100,DefenseAction.Parry,V2.UnitY));
            await Until(()=>local.Animator.CurrentClip=="parry" || observed.ParryRemaining>0,"Parry presentation");
            await Delay(.7);
            owner.SendDefense(new(101,DefenseAction.Block,V2.UnitY));
            await Until(()=>observed.Blocking,"Public block pose");
            owner.SendDefense(new(102,DefenseAction.Release,V2.UnitY));
            await Until(()=>!observed.Blocking,"Block release repair");
            Input.ParseInputEvent(new InputEventKey {PhysicalKeycode=Key.F4,Pressed=true});
            await Until(()=>world.FindChildren("UiWindow","",true,false).OfType<ProjectG.UI.UiWindow>().Any(w=>w.Visible && w.Title.Text.Contains("Эмоции")),"F4 emote menu");
            foreach(var window in world.FindChildren("UiWindow","",true,false).OfType<ProjectG.UI.UiWindow>())if(window.Visible)window.Close();
            // Use the real trainer/equip flow, so the drawn sword is proven to follow inventory authority.
            // The trainer is AOI-scoped; approach the authored arena before waiting for his reveal.
            local.MoveTo(new V2(14,-6));observer.SendMove(new(1,observer.LatestServerTick,new V2(11,-6)));
            await Until(()=>owner.QuestNpcs.Values.Any(n=>n.Name=="Радан"),"Sword trainer reveal");
            var trainer=owner.QuestNpcs.Values.First(n=>n.Name=="Радан");
            local.MoveTo(trainer.Position+new V2(-1,0));
            await Until(()=>V2.Distance(local.PredictedPosition,trainer.Position+new V2(-1,0))<.16,"Walk to trainer");local.StopMovement();
            await Delay(.6); // The server must observe the completed route before an interaction is legal.
            InventoryState bag=default;owner.InventoryReceived+=s=>bag=s;
            owner.QuestReplyReceived+=s=>GD.Print($"AVATAR_QUEST_REPLY {s.Sequence} {s.Outcome}");
            owner.SendQuest(new(1,QuestAction.TrainingSword,trainer.EntityId));
            await Until(()=>bag.Items?.Any(i=>i.Name.Contains("Тренировочный",StringComparison.OrdinalIgnoreCase))==true,"Receive training sword");
            var sword=bag.Items.First(i=>i.Name.Contains("Тренировочный",StringComparison.OrdinalIgnoreCase));
            owner.InventoryResultReceived+=s=>GD.Print($"AVATAR_EQUIP_REPLY {s.Sequence} {s.Outcome}");
            owner.SendInventory(new(1,InventoryAction.Equip,sword.Handle));
            await Until(()=>local.Animator.Armed,"Equipped sword on owner");
            // Keep both clients in AOI but never superimpose their rendered bodies in the capture.
            var peerDestination = new[]{new V2(-3,0),new V2(3,0),new V2(0,3),new V2(0,-3)}
                .Select(offset=>local.PredictedPosition+offset)
                .First(point=>owner.Navigation!.IsWalkable(point) && owner.Navigation.CanTraverse(local.PredictedPosition,point));
            uint moveSequence=10;var resendAt=0UL;
            await Until(()=>
            {
                if(Time.GetTicksMsec()>=resendAt) {observer.SendMove(new(++moveSequence,observer.LatestServerTick,peerDestination));resendAt=Time.GetTicksMsec()+250;}
                return observed.Armed;
            },"Equipped sword visible to observer");
            await Until(()=>
            {
                if(Time.GetTicksMsec()>=resendAt) {observer.SendMove(new(++moveSequence,observer.LatestServerTick,peerDestination));resendAt=Time.GetTicksMsec()+250;}
                return world.GetChildren().OfType<PlayerController>().Where(p=>p.EntityId!=local.EntityId)
                    .Any(p=>new Vector2(p.GlobalPosition.X-peerDestination.X,p.GlobalPosition.Z-peerDestination.Y).Length()<.3f);
            },"Separate observer position");
            await Delay(.7);
            local.SwordAnimation.PredictBasic(1,V2.UnitY,1);
            owner.SendAttack(new(1,owner.LatestServerTick,V2.UnitY));
            await Until(()=>local.Animator.CurrentClip.StartsWith("basic"),"Skinned attack playback");
            if(OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/animated-player-in-game.png");
            }
            await Delay(1);
            if(local.SwordAnimation.IsAnimating)throw new Exception("Attack did not return to locomotion.");
            if(OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                // Inspect the actual rendered actor, with no duplicate placed inside its silhouette.
                var camera=local.GetNode<Camera3D>("CameraRig/Camera3D");
                var target=local.Animator.GlobalPosition+Vector3.Up*.9f;
                camera.Projection=Camera3D.ProjectionType.Orthogonal;camera.Size=2.7f;
                camera.GlobalPosition=target+new Vector3(2.5f,2.2f,5);camera.LookAt(target);
                foreach(var canvas in world.FindChildren("*","CanvasLayer",true,false).OfType<CanvasLayer>())canvas.Hide();
                await Delay(.25);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/animated-player-closeup.png");
            }
            observer.QueueFree();world.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print("AVATAR_LIVE_OK: two clients, latency/loss, local/remote emotes, movement, parry/block, F4, trainer/equip, skinned attack, cleanup.");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private async Task Until(Func<bool> predicate,string action)
    {
        var deadline=Time.GetTicksMsec()+25000;
        while(!predicate()) {if(Time.GetTicksMsec()>deadline)throw new TimeoutException(action);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        GD.Print("AVATAR_STEP_OK "+action);
    }
    private async Task Delay(double seconds)
    {var end=Time.GetTicksMsec()+(ulong)(seconds*1000);while(Time.GetTicksMsec()<end)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
}
