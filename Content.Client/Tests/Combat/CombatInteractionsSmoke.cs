using Content.Shared.Network;
using Godot;
using ProjectG.Combat;
using ProjectG.Gameplay;
using ProjectG.Networking;

namespace ProjectG.Tests.Combat;

/// <summary>Native hold/release and server-approved recovery on an isolated Development character.</summary>
public partial class CombatInteractionsSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect=false; AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient"); AbilityLoadout loadout=default;
            PlayerSpawn spawn=default; network.PlayerSpawned+=v=>{if(!spawn.EntityId.IsValid)spawn=v;};
            var results=new List<AbilityResult>(); var phases=new HashSet<AbilityPhase>();
            network.AbilityLoadoutReceived+=v=>loadout=v; network.AbilityResultReceived+=results.Add;
            network.AbilityEffectReceived+=v=>{if(v.AbilityId==30)phases.Add(v.Phase);};
            network.ConnectToServer();
            await Wait(()=>network.RegionActive && loadout.Abilities is not null && network.LatestDefense is not null,"login");
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            var abilities=player.GetChildren().OfType<AbilityPresentation>().Single();
            var defense=player.GetChildren().OfType<DefensePresentation>().Single();
            await Wait(()=>player.Control==CombatControlPhase.KnockedDown,"initial grounded fall fixture");
            var start=player.PredictedPosition; Aim(new(start.X+3,player.PredictedFoot.Y,start.Y)); await Delay(.1);
            var stamina=network.LatestDefense!.Value.Stamina;
            Press(Key.Space); await Delay(.15);
            Require(defense.IsRecoverAiming && defense.RecoveryArea.Visible && !abilities.IsAiming,"Space routes to recovery footprint");
            Require(network.LatestDefense.Value.Stamina==stamina,"holding recovery spends nothing");
            Release(Key.Space);
            await Wait(()=>network.LatestDefense!.Value.QuickRecoverCooldown>0,"server recovery acceptance");
            await Wait(()=>player.Control==CombatControlPhase.None,"recovery complete");
            Require(System.Numerics.Vector2.Distance(start,player.PredictedPosition) is >1.4f and <1.6f,"actual surface roll");
            Require(loadout.Abilities.Single(a=>a.Form==AbilityForm.Dash).ReadyInSeconds==0,"independent ordinary dash cooldown");
            Require(network.LatestDefense.Value.Stamina<=stamina-14,"single recovery spend");
            Require(loadout.Abilities.Any(a=>a.Id==30),"rising is equipped in an existing slot");
            Aim(new(15,0,-14)); Press(Key.Q); await Delay(.15);
            Require(abilities.AreaVisual.Visible && abilities.AreaVisual.Shape==AbilityAreaShape.Sector && results.Count==0,"rising hold preview");
            Release(Key.Q); await Wait(()=>results.Count==1,"rising release");
            Require(results[0].Outcome==AbilityOutcome.Accepted,"rising accepted");
            await Wait(()=>phases.Contains(AbilityPhase.Recovery),"server recovery phase");
            Require(phases.Contains(AbilityPhase.Telegraph) && phases.Contains(AbilityPhase.Impact),"visible ability phases");
            // Presentation fixture: decode public wire state, keeping a remote actor's ground anchor separate from flight.
            var remote=GD.Load<PackedScene>("res://Scenes/PlayerAvatar.tscn").Instantiate<PlayerController>(); AddChild(remote);
            var remoteSpawn=spawn with {EntityId=new(999001),Position=new(12,-9)}; remote.Initialize(remoteSpawn,false,network);
            var packet=NetworkProtocol.Write(new WorldSnapshot(network.LatestServerTick+1,
                [new(remoteSpawn.EntityId,remoteSpawn.Position,0,remoteSpawn.Position,Height:spawn.Height,TargetHeight:spawn.Height,
                    AirOffset:1,Control:CombatControlPhase.Airborne,ControlRemaining:.7f)]));
            var reader=new LiteNetLib.Utils.NetDataReader(packet.CopyData()); NetworkProtocol.TryReadMessageType(reader,out _);
            Require(NetworkProtocol.TryReadWorldSnapshot(reader,out var snapshot),"flight wire state");
            remote.ApplySnapshot(snapshot.Entities[0],snapshot.ServerTick); await Delay(.2);
            Require(Math.Abs(remote.GlobalPosition.Y-spawn.Height-2)<.01,"remote visual height uses offset");
            if(OS.GetCmdlineUserArgs().Contains("--capture"))
            { await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/combat-combos/flight.png"); }
            remote.QueueFree();
            await Delay(.5); world.QueueFree(); await Delay(.15);
            GD.Print("COMBAT_INTERACTIONS_OK: real UDP, hold/release Quick Recover, surface roll, single cost, independent cooldown, rising sector and windup/impact/recovery."); GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static void Require(bool condition,string step) {if(!condition)throw new InvalidOperationException(step);}
    private void Aim(Vector3 point)=>Input.WarpMouse(GetViewport().GetCamera3D().UnprojectPosition(point));
    private static void Press(Key key)=>Input.ParseInputEvent(new InputEventKey {PhysicalKeycode=key,Keycode=key,Pressed=true});
    private static void Release(Key key)=>Input.ParseInputEvent(new InputEventKey {PhysicalKeycode=key,Keycode=key,Pressed=false});
    private async Task Delay(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> ready,string step)
    {var until=Time.GetTicksMsec()+15000;while(!ready()){if(Time.GetTicksMsec()>until)throw new TimeoutException(step);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}}
}
