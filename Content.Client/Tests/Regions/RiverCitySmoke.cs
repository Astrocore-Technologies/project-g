using Content.Shared.Navigation;
using Godot;
using ProjectG.Regions;
using ProjectG.Regions.Authoring;

namespace ProjectG.Tests.Regions;

public partial class RiverCitySmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var region=GD.Load<PackedScene>("res://Scenes/Regions/RiverCity/RiverCity.tscn").Instantiate<RegionRoot>();
            AddChild(region);
            var baked=FlatGeometryBake.Bake(region);
            var exported=FlatRegionGeometry.Parse(File.ReadAllText(ProjectSettings.GlobalizePath("res://../Content.Server/Data/Regions/river-city.json")));
            Require(baked.Hash==exported.Hash,"Stale city navigation export.");
            var grid=baked.CreateGrid(); var pathfinder=new NavigationPathfinder(grid); var path=new List<System.Numerics.Vector2>();
            foreach(var marker in region.GetNode("AuthoringAnchors").GetChildren().OfType<RegionMarker>())
                Require(pathfinder.TryFindPath(new(0,18),new(marker.Position.X,marker.Position.Z),path),$"Unreachable {marker.Name}: {marker.Position}");
            foreach(var target in new System.Numerics.Vector2[]{new(24.5f,-8),new(27.5f,-8),new(30.5f,-8),new(-21,13)})
                Require(pathfinder.TryFindPath(new(0,18),target,path),$"Unreachable lane {target}");
            Require(!grid.IsWalkable(new(0,-27))&&!grid.IsWalkable(new(-7,-10))&&!grid.IsWalkable(new(0,0)),"Walls/buildings/fountain must block movement.");
            var preview=region.GetNode<BlockoutPreview>("Preview");
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            for(var i=0;i<6;i++)
            {
                preview.ShowPoint(i);
                var point=preview.Viewpoints.GetChild<Node3D>(i).GlobalPosition;
                var space=region.GetWorld3D().DirectSpaceState;
                Require(space.IntersectRay(PhysicsRayQueryParameters3D.Create(point+Vector3.Up*8,point-Vector3.Up,1)).Count>0,"No floor.");
                Require(space.IntersectRay(PhysicsRayQueryParameters3D.Create(point+Vector3.Up*8,point-Vector3.Up,2)).Count==0,$"Blocked preview {i}.");
            }
            if(OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                preview.GetNode<CanvasLayer>("PreviewControls").Hide();
                preview.ShowOverview();
                await Capture("overview");
                preview.OverviewLabels.Hide(); preview.Camera.Position=new(44,63,54); preview.Camera.Size=87;
                preview.Camera.LookAt(new(0,0,0));
                await Capture("perspective");
                preview.ShowPoint(0); await Capture("market");
                preview.ShowPoint(2); await Capture("arena");
            }
            region.QueueFree();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Require(!GodotObject.IsInstanceValid(region),"Region lifecycle leaked.");
            GD.Print("RIVER_CITY_OK: scene/export match, all anchors and archery lanes reachable, walls block, six cameras, lifecycle.");
            GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        Require(GetViewport().GetTexture().GetImage().SavePng($"res://../.artifacts/river-city-{name}.png")==Error.Ok,"Capture failed.");
    }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
}
