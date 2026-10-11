using Content.Shared.Network;
using Godot;
using ProjectG.Combat;
using ProjectG.Gameplay;
using ProjectG.Networking;
using V2=System.Numerics.Vector2;
namespace ProjectG.Actors;

/// <summary>Server-driven NPC windup, player death, developer revive and AOI teardown on an isolated profile.</summary>
public partial class CapsuleLifecycleLive : Node
{
    public override async void _Ready()
    {
        try
        {
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect=false;
            var network=world.GetNode<NetworkClient>("NetworkClient");
            network.DevelopmentIdentityProfile="caps-life-"+Guid.NewGuid().ToString("N")[..10];
            AddChild(world); network.ConnectToServer();
            PlayerController? player=null;
            await Until(()=>network.RegionActive && (player=Local(world)) is not null,"city login");
            player!.MoveTo(new(-36,0));
            await Until(()=>network.CurrentRegion?.Region=="outskirts" && network.RegionActive && (player=Local(world)) is not null,"outskirts arrival");
            var windupSeen=false; var strikeSeen=false;
            network.NpcWindupReceived+=state=> { if(state.RemainingSeconds>0) windupSeen=true; };
            player!.MoveTo(new(9,2));
            await Until(()=>world.GetChildren().OfType<NpcPresentation>().Any(),"monster AOI reveal");
            var npc=world.GetChildren().OfType<NpcPresentation>().First();
            Check(npc.Visual is CapsuleActorVisual,"Live NPC is not a capsule");
            var root=(CapsuleActorVisual)player.Animator;
            await Until(()=>
            {
                if(npc.Visual.CurrentClip=="npc_strike") strikeSeen=true;
                return !player.IsAlive;
            },"authoritative NPC damage kills player",60);
            Check(windupSeen && strikeSeen,"NPC public windup/strike did not animate");
            await Until(()=>root.CurrentClip=="dead_back","capsule grounded death");
            Check(network.CanDevelopmentRevive,"Test requires advertised developer revive");
            network.SendDevelopmentRevive(new(1));
            await Until(()=>player.IsAlive && root.CurrentClip is not ("death_back" or "dead_back"),"server revive resets capsule");
            world.QueueFree(); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Check(GetTree().GetNodesInGroup("CombatTargets").Count==0,"AOI/world teardown leaked combat targets");
            GD.Print("CAPSULE_LIFECYCLE_LIVE_OK: actual NPC capsule, server windup/strike, HP death, grounded pose, server revive, region/AOI cleanup.");
            GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static PlayerController? Local(Node world)=>world.GetChildren().OfType<PlayerController>()
        .FirstOrDefault(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
    private async Task Until(Func<bool> predicate,string step,int seconds=35)
    {
        var deadline=Time.GetTicksMsec()+(ulong)(seconds*1000);
        while(!predicate())
        {
            if(Time.GetTicksMsec()>deadline) throw new TimeoutException(step);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        GD.Print("CAPSULE_LIFE_STEP_OK "+step);
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
}