using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using V2 = System.Numerics.Vector2;

namespace ProjectG.Tests.Regions;

/// <summary>Isolated smoke test of the production world, picking and height snapshots.</summary>
public partial class LinvelBeechLiveSmoke : Node
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
            _network.DevelopmentIdentityProfile = "beech-smoke-"+Guid.NewGuid().ToString("N")[..12];
            _network.SnapshotReceived += Snapshot;
            AddChild(_world);
            await Wait(() => _network.RegionActive && _network.CurrentRegion?.Region == "linvel_beech");
            _player = _world.GetChildren().OfType<PlayerController>().Single(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            await Wait(() => _confirmed.EntityId == _player.EntityId);
            var terrain=_world.GetNode<Node3D>("BeechValley/Geometry/Terrain");
            if(!terrain.IsPhysicsProcessing() || terrain.Call("get_camera").AsGodotObject()!=_player.GetNode<Camera3D>("CameraRig/Camera3D"))
                throw new InvalidOperationException("Terrain3D did not bind the production camera.");
            if (_world.GetNodeOrNull("BeechValley/RegionPreviewCamera") is not null)
                throw new InvalidOperationException("Inspection camera leaked into the network game.");
            await ValidateForestPicking();
            ValidatePhysicalCover();
            var initial = _confirmed.Position;
            var initialHeight = _confirmed.Height;
            var goal = initial+new V2(7,-5);
            var worldPoint = ProjectG.Regions.PublicSurfaceAnchors.Position(_network,goal);
            var camera = GetViewport().GetCamera3D();
            _player._UnhandledInput(new InputEventMouseButton { ButtonIndex=MouseButton.Right,Pressed=true,
                Position=camera.UnprojectPosition(worldPoint) });
            await Wait(() => V2.Distance(_confirmed.Position,goal)<.3f);
            if (Math.Abs(_player.GlobalPosition.Y-(_confirmed.Height+1))>.3f)
                throw new InvalidOperationException("Rendered actor differs from server height.");
            if (!_player.MoveTo(initial,initialHeight)) throw new InvalidOperationException("Return rejected.");
            await Wait(() => V2.Distance(_confirmed.Position,initial)<.15f);
            GD.Print("LINVEL_BEECH_LIVE_OK: authored region loaded, production RMB collider pick, authoritative movement and return.");
            _network.SnapshotReceived -= Snapshot;
            _world.QueueFree();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Snapshot(WorldSnapshot value)
    {
        if (_player is null) return;
        foreach (var state in value.Entities)
            if (state.EntityId == _player.EntityId) _confirmed=state;
    }

    private async Task ValidateForestPicking()
    {
        var camera = GetViewport().GetCamera3D();
        var original = camera.GlobalTransform;
        V2[] points = [new(-425,330),new(-414,321),new(-330,260),new(-265,230),new(-108,129),
            new(185,-85),new(-295,-105),new(325,-330),new(80,-215),
            // Largest measured native/baked floor difference on the 16 m grid.
            new(-400,-448)];
        try
        {
            foreach (var point in points)
            {
                var terrain=_world.GetNode<Node3D>("BeechValley/Geometry/Terrain");
                var native=terrain.Get("data").AsGodotObject().Call("get_height",new Vector3(point.X,0,point.Y)).AsSingle();
                camera.GlobalPosition=new Vector3(point.X,native+12,point.Y+12); camera.LookAt(new Vector3(point.X,native,point.Y));
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                // Use the production floor layer and physical ray, including
                // places where Recast height differs slightly from the collider.
                var ray = PhysicsRayQueryParameters3D.Create(new(point.X,100,point.Y),new(point.X,-50,point.Y),1,
                    new Godot.Collections.Array<Rid>{_player!.GetRid()});
                var hit = _world.GetWorld3D().DirectSpaceState.IntersectRay(ray);
                if (hit.Count==0) throw new InvalidOperationException($"Missing terrain at {point}");
                var physical = hit["position"].AsVector3();
                camera.GlobalPosition = physical+new Vector3(0,12,12); camera.LookAt(physical);
                if (!_player!.TryCursorSurface(out var picked,out var height,camera.UnprojectPosition(physical)) ||
                    V2.Distance(point,picked)>.01f || Math.Abs(height-physical.Y)>.501f)
                {
                    var cursor = camera.UnprojectPosition(physical);
                    var rayOrigin = camera.ProjectRayOrigin(cursor);
                    var rayEnd = rayOrigin + camera.ProjectRayNormal(cursor)*500;
                    var cursorHit = _world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(rayOrigin,rayEnd,1,
                        new Godot.Collections.Array<Rid>{_player.GetRid()}));
                    var column = new List<Content.Shared.Navigation.SurfaceLocation>();
                    _network.Navigation!.Surface!.SampleColumn(point.X,point.Y,column);
                    throw new InvalidOperationException($"Forest RMB pick failed at {point}, native={native}, physical={physical}, camera={camera.GlobalPosition}, cursor={cursor}, rayHit={cursorHit}, serverHeights={string.Join(',',column.Select(p=>p.Position.Y))}");
                }
            }
            GD.Print($"LINVEL_BEECH_PICK_OK: {points.Length} physical forest picks.");
        }
        finally { camera.GlobalTransform=original; }
    }

    private void ValidatePhysicalCover()
    {
        var trunk=_world.GetNode<Node3D>("BeechValley/LandmarkTrees/LandmarkBeech3").GlobalPosition;
        var newTrunk=_world.GetNode<Node3D>("BeechValley/AuthoredForestBands/YoungWesternEdge/Beech000").GlobalPosition;
        var bridge=new Vector3(-108,28.1f,129);
        var normal=new Vector3(.6f,0,.8f);
        var terrain=_world.GetNode<Node3D>("BeechValley/Geometry/Terrain").Get("data").AsGodotObject();
        Vector3 Ground(float x,float z)=>new(x,terrain.Call("get_height",new Vector3(x,0,z)).AsSingle(),z);
        // Keep the probe within the same bounded 128 m visibility contract as attacks.
        var west=Ground(-312,-290);var east=Ground(-188,-290);
        var probes=new[] { (trunk-new Vector3(3,0,0),trunk+new Vector3(3,0,0),false,"beech trunk"),
            (bridge+normal*3,bridge-normal*3,false,"bridge railing"),
            (bridge+new Vector3(-4,0,3),bridge+new Vector3(4,0,-3),true,"bridge lane"),
            (west,east,false,"ridge rock shoulder"),
            (new Vector3(west.X,85,west.Z),new Vector3(east.X,85,east.Z),true,"above ridge"),
            (Ground(newTrunk.X-3,newTrunk.Z),Ground(newTrunk.X+3,newTrunk.Z),false,"new physical beech"),
            (Ground(newTrunk.X-3,newTrunk.Z+3),Ground(newTrunk.X+3,newTrunk.Z+3),true,"beside new beech") };
        foreach(var (from,to,clear,label) in probes)
        {
            var hit=_world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from+Vector3.Up,to+Vector3.Up,5));
            var authoritative=_network.Navigation!.ClearAttack(new(from.X,from.Y,from.Z),new(to.X,to.Y,to.Z));
            if ((hit.Count==0)!=clear || authoritative!=clear)
                throw new InvalidOperationException($"Cover mismatch {label}: physicalClear={hit.Count==0}, serverClear={authoritative}");
        }
        GD.Print($"LINVEL_BEECH_COVER_OK: {probes.Length} native/Jolt and server LOS comparisons.");
    }

    private async Task Wait(Func<bool> done)
    {
        var end = Time.GetTicksMsec()+30000;
        while (!done())
        {
            if (Time.GetTicksMsec()>end)
                throw new TimeoutException($"Region={_network.CurrentRegion?.Region}, foot={_confirmed.Position}/{_confirmed.Height}, connection={_network.LastConnectionError}");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
