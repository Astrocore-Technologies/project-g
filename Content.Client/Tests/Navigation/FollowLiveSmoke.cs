using Content.Shared.Navigation;
using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
using V2=System.Numerics.Vector2;

namespace ProjectG.Tests.Navigation;

public partial class FollowLiveSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            PcInput.Initialize($"user://m4-live-{Guid.NewGuid():N}/input.cfg");
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();world.AutoConnect=false;
            var network=world.GetNode<NetworkClient>("NetworkClient");network.DevelopmentIdentityProfile="m4-follower-"+Guid.NewGuid().ToString("N")[..12];
            network.SimulateNetworkConditions=false;
            EntitySnapshot own=default;FollowState? follow=null;
            network.SnapshotReceived+=s=>{foreach(var state in s.Entities)if(network.KnownPlayers.TryGetValue(state.EntityId,out var p)&&p.PlayerId==network.LocalPlayerId)own=state;};
            network.FollowReceived+=s=>follow=s;
            var moves=0;network.MovementSent+=_=>moves++;
            AddChild(world);network.ConnectToServer();
            await Wait(()=>network.RegionActive&&own.EntityId.IsValid,"world baseline");
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.EntityId==own.EntityId);
            var other=new NetworkClient{DevelopmentIdentityProfile="m4-target-"+Guid.NewGuid().ToString("N")[..12],SimulateNetworkConditions=false};
            other.NavigationReceived+=_=>other.ConfirmRegionScene();
            EntitySnapshot target=default;
            other.SnapshotReceived+=s=>{foreach(var state in s.Entities)if(other.KnownPlayers.TryGetValue(state.EntityId,out var p)&&p.PlayerId==other.LocalPlayerId)target=state;};
            AddChild(other);other.ConnectToServer();
            await Wait(()=>other.RegionActive&&target.EntityId.IsValid&&network.KnownPlayers.ContainsKey(target.EntityId),"second client baseline");
            foreach(var peer in new[]{network,other}){peer.SimulateNetworkConditions=true;peer.SimulatedMinLatencyMs=60;peer.SimulatedMaxLatencyMs=90;peer.SimulatedPacketLossPercent=3;peer.ApplyNetworkSimulation();}
            var start=own.Position;var goal=Nearby(other.Navigation!,target,other.KnownPlayers[target.EntityId].Movement,8);
            var presentation=player.GetChildren().OfType<FollowPresentation>().Single();
            var camera=GetViewport().GetCamera3D();var point=camera.UnprojectPosition(new(target.Position.X,target.Height+1,target.Position.Y));
            var before=moves;
            presentation._UnhandledInput(new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Right,Position=point});
            var view=presentation.GetChild<Control>(0);Check(view.GetNode<PanelContainer>("%Menu").Visible,"Context menu did not open on visible player");
            Check(moves==before,"Context preview issued movement");
            view.GetNode<Button>("%Follow").EmitSignal(BaseButton.SignalName.Pressed);
            await Wait(()=>follow?.Phase==FollowPhase.Active,"Follow accepted");
            other.SendMove(new(1,1,goal,TargetHeight:target.Height,GeometryHash:other.Navigation!.SurfaceHash,InputOrder:other.NextInputOrder()));
            await Wait(()=>V2.Distance(own.Position,start)>1&&V2.Distance(target.Position,goal)<.1f&&V2.Distance(own.Position,target.Position)<3.4f,"ordinary server following");
            await ToSignal(GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);
            Check(V2.Distance(player.PredictedPosition,own.Position)<.5f,"Follow prediction drift");
            Check(player.PredictionFrameCount<20,"Follow acknowledgement did not prune prediction history");
            Check(moves==before,"Follow created client movement heartbeats");
            Check(PcInput.Assign(PcInput.Stop,new(Key.F8),out _),"Cannot map Stop");
            player._UnhandledInput(new InputEventKey{PhysicalKeycode=Key.F8,Pressed=true});
            await Wait(()=>follow?.Phase==FollowPhase.Stopped&&V2.Distance(own.Target,own.Position)<.001f,"remapped Stop cancels Follow");
            GameUi.Navigate("settings");await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Check(GameUi.GameplayModalOpen,"In-game settings route not opened");
            await ToSignal(GetTree().CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);var afterMenu=moves;
            player._UnhandledInput(new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Right,Position=point});
            await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
            Check(moves==afterMenu,"Settings leaked movement input");
            GD.Print("FOLLOW_LIVE_SMOKE_OK: real Beech map, two clients, UDP 60-90ms/3% loss, context preview, owner Follow, no input heartbeat, prediction/ack, remapped Stop and actual settings input isolation.");
            GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private static V2 Nearby(NavigationGrid grid,EntitySnapshot state,Content.Shared.Movement.MovementSettings settings,float distance)
    {
        foreach(var direction in new[]{V2.UnitY,-V2.UnitY,V2.UnitX,-V2.UnitX})
        {
            var target=state.Position+direction*distance;
            var motion=new NavigationMover(grid,settings,new(grid),state.Position,state.Height);
            if(motion.TrySetTarget(target,state.Height))return target;
        }
        throw new InvalidOperationException("No nearby test route");
    }
    private async Task Wait(Func<bool> condition,string stage)
    {
        var deadline=Time.GetTicksMsec()+40000;
        while(!condition()){if(Time.GetTicksMsec()>deadline)throw new TimeoutException(stage);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
