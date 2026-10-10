using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using V2 = System.Numerics.Vector2;

namespace ProjectG.Tests.Regions;

/// <summary>Exercises the actual client, physical RMB picking and authoritative height snapshots.</summary>
public partial class TerrainLiveSmoke : Node
{
    private WorldController _world=null!;
    private NetworkClient _network=null!;
    private EntitySnapshot _confirmed;
    private PlayerController Local() => _world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);

    public override async void _Ready()
    {
        try
        {
            _world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            _network=_world.GetNode<NetworkClient>("NetworkClient");
            _network.DevelopmentIdentityProfile="terrain-smoke-"+Guid.NewGuid().ToString("N")[..12];
            _network.SnapshotReceived+=Snapshot;AddChild(_world);
            await Wait(()=>_network.RegionActive && _network.CurrentRegion?.Region=="terrain_test");
            var character=_network.LocalPlayerId;
            // The first movement uses the production RMB input path and an actual scene collider.
            var player=Local();var camera=GetViewport().GetCamera3D();
            player._UnhandledInput(new InputEventMouseButton { ButtonIndex=MouseButton.Right,Pressed=true,
                Position=camera.UnprojectPosition(new(-10,0,2)) });
            await Wait(()=>V2.Distance(_confirmed.Position,new(-10,2))<.3f);
            await Move(new(-10,-11),4);
            await Move(new(6,-6),4);
            if (OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/terrain-integration/live.png");
            }
            await Move(new(10,9),-3);
            Local().MoveTo(new(16,14),.1f);
            await Wait(()=>_network.RegionActive && _network.CurrentRegion?.Region=="dungeon_test");
            await Move(new(7,-6),4);
            Local().MoveTo(new(-17,14),.1f);
            await Wait(()=>_network.RegionActive && _network.CurrentRegion?.Region=="terrain_test");
            if(_network.LocalPlayerId!=character)throw new InvalidOperationException("Character changed during travel.");
            _network.SnapshotReceived-=Snapshot;_world.QueueFree();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print("TERRAIN_LIVE_OK: RMB, hill, bridge, pit, shared dungeon/gallery/return and character identity.");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private void Snapshot(WorldSnapshot value)
    {
        foreach(var state in value.Entities)if(_world.GetChildren().OfType<PlayerController>().Any(p=>p.EntityId==state.EntityId && p.GetNode<Camera3D>("CameraRig/Camera3D").Current))_confirmed=state;
    }
    private async Task Move(V2 goal,float height)
    {
        if(!Local().MoveTo(goal,height))throw new InvalidOperationException($"Unreachable {goal}/{height}.");
        await Wait(()=>V2.Distance(_confirmed.Position,goal)<.15f && Math.Abs(_confirmed.Height-height)<.2f);
        if(Math.Abs(Local().GlobalPosition.Y-(_confirmed.Height+1))>.3f)throw new InvalidOperationException("Rendered avatar differs from authority height.");
    }
    private async Task Wait(Func<bool> done)
    {
        var end=Time.GetTicksMsec()+20000;
        while(!done())
        {
            if(Time.GetTicksMsec()>end)throw new TimeoutException($"Region={_network.CurrentRegion?.Region}, foot={_confirmed.Position}/{_confirmed.Height}, active={_network.RegionActive}, connection={_network.LastConnectionError}");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
