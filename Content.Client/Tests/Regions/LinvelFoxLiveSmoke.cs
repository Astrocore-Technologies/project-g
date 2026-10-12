
using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using V2 = System.Numerics.Vector2;

namespace ProjectG.Tests.Regions;

/// <summary>Runs the real client against an isolated server and checks Jolt ground/colliders.</summary>
public partial class LinvelFoxLiveSmoke : Node
{
    private WorldController _world = null!;
    private NetworkClient _network = null!;
    private PlayerController? _player;
    private EntitySnapshot _confirmed;

    public override async void _Ready()
    {
        try
        {
            _world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            _network = _world.GetNode<NetworkClient>("NetworkClient");
            _network.DevelopmentIdentityProfile = "fox-smoke-"+Guid.NewGuid().ToString("N")[..12];
            _network.SnapshotReceived += Snapshot;
            AddChild(_world);
            await Wait(() => _network.RegionActive && _network.CurrentRegion?.Region == "linvel_fox_hills");
            _player = _world.GetChildren().OfType<PlayerController>().Single(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            await Wait(() => _confirmed.EntityId == _player.EntityId);
            var terrain=_world.GetNode<Node3D>("FoxHills/Geometry/Terrain");
            if(!terrain.IsPhysicsProcessing() || terrain.Call("get_camera").AsGodotObject()!=_player.GetNode<Camera3D>("CameraRig/Camera3D"))
                throw new InvalidOperationException("Terrain3D did not bind the production local camera.");
            await ValidateObjectPhysics();
            ValidatePicking();
            var initial = _confirmed.Position;
            var initialHeight = _confirmed.Height;
            var goal = initial+new V2(7,-5);
            var point=ProjectG.Regions.PublicSurfaceAnchors.Position(_network,goal);
            var camera=GetViewport().GetCamera3D();
            _player._UnhandledInput(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true,Position=camera.UnprojectPosition(point)});
            await Wait(()=>V2.Distance(_confirmed.Position,goal)<.3f);
            if(Math.Abs(_player.GlobalPosition.Y-(_confirmed.Height+1))>.3f)
                throw new InvalidOperationException("Rendered actor differs from server height.");
            if(!_player.MoveTo(initial,initialHeight)) throw new InvalidOperationException("Return rejected.");
            await Wait(()=>V2.Distance(_confirmed.Position,initial)<.15f);
            GD.Print("LINVEL_FOX_LIVE_OK: production scene, camera, RMB movement, authoritative height and return.");
            _network.SnapshotReceived-=Snapshot;
            _world.QueueFree();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GetTree().Quit();
        }
        catch(Exception error){ GD.PushError(error.ToString());GetTree().Quit(1); }
    }

    private void Snapshot(WorldSnapshot snapshot)
    {
        if(_player is null)return;
        foreach(var state in snapshot.Entities) if(state.EntityId==_player.EntityId)_confirmed=state;
    }
    private void ValidatePicking()
    {
        var camera=GetViewport().GetCamera3D();var original=camera.GlobalTransform;
        V2[] points=[new(-350,420),new(-290,330),new(40,130),new(40,95),new(40,61),new(40,20),
            new(40,-40),new(40,-145),new(-230,200),new(-230,150),new(280,25),new(430,150)];
        try
        {
            foreach(var p in points)
            {
                var ray=PhysicsRayQueryParameters3D.Create(new(p.X,140,p.Y),new(p.X,-50,p.Y),1,
                    new Godot.Collections.Array<Rid> { _player!.GetRid() });
                var hit=_world.GetWorld3D().DirectSpaceState.IntersectRay(ray);
                if(hit.Count==0)throw new InvalidOperationException($"Missing physical terrain at {p}");
                var physical=hit["position"].AsVector3();
                camera.GlobalPosition=physical+new Vector3(0,12,12);camera.LookAt(physical);
                if(!_player!.TryCursorSurface(out var picked,out var height,camera.UnprojectPosition(physical)) ||
                    V2.Distance(p,picked)>.015f || Math.Abs(height-physical.Y)>.501f)
                    throw new InvalidOperationException($"Fox RMB pick failed at {p}, physical height={physical.Y}, picked={picked}/{height}");
            }
            GD.Print($"LINVEL_FOX_PICK_OK: {points.Length} Jolt picks on roads, ramps and plateaux.");
        }
        finally{camera.GlobalTransform=original;}
    }
    private async Task ValidateObjectPhysics()
    {
        var region=_world.GetNode<Node3D>("FoxHills");
        if(region.GetNode<Node3D>("Geometry").GetChildren().OfType<StaticBody3D>().Any())
            throw new InvalidOperationException("Detached object collider under Geometry.");
        var actor=region.GetNode<Node3D>("Places/WatchHill/Watchpost");
        var body=actor.GetNode<StaticBody3D>("BuildingBody");
        var original=actor.Transform;
        try
        {
            // Real physics queries verify that a common placement root owns model and shape.
            actor.Position+=new Vector3(37,20,17);actor.RotateY(.5f);actor.Scale*=1.25f;
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            var center=body.GetNode<CollisionShape3D>("Collision").GlobalPosition;
            var hit=region.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(center+Vector3.Up*12,center-Vector3.Up*12,4));
            if(hit.Count==0 || hit["collider"].AsGodotObject()!=body)throw new InvalidOperationException("Watchpost collider did not follow its placement.");
        }
        finally{actor.Transform=original;await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);}
        GD.Print("LINVEL_FOX_OBJECT_PHYSICS_OK: nested shape follows translation, yaw and uniform scale.");
    }
    private async Task Wait(Func<bool> done)
    {
        var end=Time.GetTicksMsec()+30000;
        while(!done())
        {
            if(Time.GetTicksMsec()>end)throw new TimeoutException($"Region={_network.CurrentRegion?.Region}, foot={_confirmed.Position}/{_confirmed.Height}, connection={_network.LastConnectionError}");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
