using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;

namespace ProjectG.Tests.Regions;

/// <summary>Real network client and gameplay scene, against an isolated Development server.</summary>
public partial class RiverCityLiveSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient");
            bool Ready(string region)=>network.CurrentRegion?.Region==region && network.Navigation is not null &&
                world.GetChildren().OfType<PlayerController>().Any(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            await Until(()=>Ready("river_city"),"City login");
            if(network.CurrentRegion?.AdditionalGates?.Length!=1 || network.Navigation?.CellCount!=5120)
                throw new InvalidOperationException("Expected city navigation and both gate markers.");
            if(world.FindChildren("Preview","",true,false).Count!=0) throw new InvalidOperationException("Editor preview leaked into gameplay.");
            if(OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                var settle=Time.GetTicksMsec()+700;
                while(Time.GetTicksMsec()<settle) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/river-city-live.png");
            }
            uint sequence=1000;
            foreach(var (destination,x,z) in new (string,float,float)[]{("prototype",0,29),("river_city",-12,30),("outskirts",-36,0),("river_city",-10,13)})
            {
                var from=network.CurrentRegion!.Value.Region;
                var nextSend=0UL;
                await Until(()=>
                {
                    if(network.CurrentRegion?.Region==from && Time.GetTicksMsec()>=nextSend)
                    {
                        network.SendMove(new(++sequence,network.LatestServerTick,new(x,z))); nextSend=Time.GetTicksMsec()+250;
                    }
                    return Ready(destination);
                },$"Gate {from} -> {destination}");
                GD.Print($"CITY_GATE_OK: {from} -> {destination}");
            }
            world.QueueFree(); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print("RIVER_CITY_LIVE_OK: new-character spawn, both gate round trips, actual scene swaps and teardown."); GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private async Task Until(Func<bool> predicate,string operation)
    {
        var deadline=Time.GetTicksMsec()+30000;
        while(!predicate())
        {
            if(Time.GetTicksMsec()>deadline) throw new TimeoutException(operation);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
